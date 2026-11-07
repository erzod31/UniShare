import java.util.zip.ZipFile

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
}

val signingVariables = listOf(
    "UNISHARE_ANDROID_KEYSTORE",
    "UNISHARE_ANDROID_STORE_PASSWORD",
    "UNISHARE_ANDROID_KEY_ALIAS",
    "UNISHARE_ANDROID_KEY_PASSWORD",
)
val signingValues = signingVariables.associateWith { providers.environmentVariable(it).orNull }
val signingMode = providers.environmentVariable("UNISHARE_ANDROID_SIGNING_MODE").orNull
    ?: if (signingValues.values.any { it != null }) "production" else "legacy"
require(signingMode in setOf("legacy", "production")) {
    "UNISHARE_ANDROID_SIGNING_MODE must be legacy or production."
}
require(signingMode != "legacy" || signingValues.values.all { it == null }) {
    "Production credentials cannot be used with legacy signing."
}
if (signingMode == "production") {
    require(signingValues.values.all { !it.isNullOrEmpty() }) {
        "Production signing requires all four UNISHARE_ANDROID signing variables."
    }
    require(file(checkNotNull(signingValues["UNISHARE_ANDROID_KEYSTORE"])).isFile) {
        "The configured production keystore does not exist."
    }
}

android {
    namespace = "org.unishare.app"
    compileSdk = 37

    defaultConfig {
        applicationId = "org.unishare.app"
        minSdk = 26
        targetSdk = 37
        versionCode = 39
        versionName = "0.6.26"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    signingConfigs {
        if (signingMode == "production") {
            create("production") {
                storeFile = file(checkNotNull(signingValues["UNISHARE_ANDROID_KEYSTORE"]))
                storePassword = signingValues["UNISHARE_ANDROID_STORE_PASSWORD"]
                keyAlias = signingValues["UNISHARE_ANDROID_KEY_ALIAS"]
                keyPassword = signingValues["UNISHARE_ANDROID_KEY_PASSWORD"]
            }
        }
    }

    buildTypes {
        debug {
            // Los recorridos automatizados nunca comparten datos con la instalación del usuario.
            applicationIdSuffix = ".qa"
        }
        release {
            // El modo legacy conserva la firma lateral histórica. Production falla si falta la clave.
            signingConfig = signingConfigs.getByName(if (signingMode == "production") "production" else "debug")
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    sourceSets {
        getByName("main").assets.srcDir("../../../shared")
        getByName("test").resources.directories.add("../../../tests/fixtures")
        getByName("androidTest").assets.directories.add("../../../tests/fixtures")
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2026.09.00")
    implementation(composeBom)
    implementation("androidx.activity:activity-compose:1.13.0")
    implementation("androidx.core:core-ktx:1.19.1")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.work:work-runtime-ktx:2.12.0")
    testImplementation("junit:junit:4.13.2")
    // Real JSON on the JVM; Android uses its built-in org.json, never this test-only JAR.
    testImplementation("org.json:json:20260814")
    androidTestImplementation("androidx.test:runner:1.7.0")
    androidTestImplementation("androidx.test.ext:junit:1.3.0")
    androidTestImplementation("androidx.test.uiautomator:uiautomator:2.4.0")
}

val noticesDirectory = layout.buildDirectory.dir("generated/thirdPartyAssets")
val licenseRoot = rootProject.layout.projectDirectory.dir("../../licenses").asFile
val projectLicense = rootProject.layout.projectDirectory.file("../../LICENSE").asFile
val thirdPartyNotices = rootProject.layout.projectDirectory.file("../../THIRD_PARTY_NOTICES.md").asFile
abstract class PrepareThirdPartyNotices : DefaultTask() {
    @get:InputFiles
    @get:PathSensitive(PathSensitivity.RELATIVE)
    abstract val licenseFiles: org.gradle.api.file.ConfigurableFileCollection
    @get:Input
    abstract val runtimeDependencies: org.gradle.api.provider.ListProperty<String>
    @get:InputFiles
    @get:PathSensitive(PathSensitivity.NAME_ONLY)
    abstract val runtimeArchives: org.gradle.api.file.ConfigurableFileCollection
    @get:OutputDirectory
    abstract val outputDirectory: org.gradle.api.file.DirectoryProperty

    @TaskAction
    fun generate() {
        val destination = outputDirectory.get().dir("licenses").asFile.apply { mkdirs() }
        licenseFiles.files.filter { it.isFile }.forEach { it.copyTo(destination.resolve(it.name), overwrite = true) }
        runtimeArchives.files.forEach { artifact ->
            ZipFile(artifact).use { archive ->
                archive.entries().asSequence().filter { entry ->
                    !entry.isDirectory && entry.name.substringAfterLast('/').uppercase()
                        .matches(Regex("(?:LICENSE|NOTICE|COPYING)(?:[._-].*)?"))
                }.forEach { entry ->
                    val name = entry.name.replace(Regex("[^A-Za-z0-9_.-]"), "_")
                    val target = destination.resolve("embedded/${artifact.name}/$name")
                    target.parentFile.mkdirs()
                    archive.getInputStream(entry).use { input -> target.outputStream().use { input.copyTo(it) } }
                }
            }
        }
        val inventory = runtimeDependencies.get().map { module ->
            val group = module.substringBefore(':')
            val apache = group.startsWith("androidx.") || group in setOf(
                "org.jetbrains", "org.jetbrains.kotlin", "org.jetbrains.kotlinx",
                "com.google.guava", "com.google.errorprone", "org.jspecify",
            )
            check(apache) { "Review the distribution license for $module before packaging." }
            "$module\tApache-2.0"
        }
        destination.resolve("inventory-android.txt").writeText(
            "Android release runtime dependencies; test dependencies are excluded.\n" +
                inventory.joinToString("\n", postfix = "\n"),
        )
    }
}
val prepareThirdPartyNotices = tasks.register<PrepareThirdPartyNotices>("prepareThirdPartyNotices") {
    licenseFiles.from(fileTree(licenseRoot), projectLicense, thirdPartyNotices)
    outputDirectory.set(noticesDirectory)
}
androidComponents.onVariants { variant ->
    if (variant.buildType == "release") {
        val runtime = variant.runtimeConfiguration
        prepareThirdPartyNotices.configure {
            runtimeArchives.from(runtime)
            runtimeDependencies.set(runtime.incoming.artifacts.resolvedArtifacts.map { artifacts ->
                artifacts.mapNotNull {
                    it.id.componentIdentifier as? org.gradle.api.artifacts.component.ModuleComponentIdentifier
                }.distinct().map { it.toString() }.sorted()
            })
        }
    }
    variant.sources.assets?.addGeneratedSourceDirectory(prepareThirdPartyNotices) { it.outputDirectory }
}
