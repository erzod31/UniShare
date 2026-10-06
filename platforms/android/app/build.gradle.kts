import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "org.unishare.app"
    compileSdk = 36

    defaultConfig {
        applicationId = "org.unishare.app"
        minSdk = 26
        targetSdk = 36
        versionCode = 29
        versionName = "0.6.16"
    }

    buildTypes {
        release {
            // Distribución lateral actual: conserva la clave de actualización de las versiones previas,
            // pero elimina código y recursos no usados para facilitar la transferencia del APK.
            signingConfig = signingConfigs.getByName("debug")
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
}

kotlin {
    compilerOptions {
        jvmTarget.set(JvmTarget.JVM_17)
    }
}

dependencies {
    // Última BOM estable compatible con compileSdk 36 / AGP 8.13.
    // Compose 1.12 (BOM 2026.08+) exige API 37 y AGP 9.1.
    val composeBom = platform("androidx.compose:compose-bom:2026.06.01")
    implementation(composeBom)
    implementation("androidx.activity:activity-compose:1.13.0")
    // Core 1.19.1 requires compileSdk 37 and AGP 9.1; this release deliberately remains on
    // the stable API 36 toolchain. Re-evaluate these three versions together.
    //noinspection GradleDependency
    implementation("androidx.core:core-ktx:1.18.0")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.work:work-runtime-ktx:2.12.0")
    testImplementation("junit:junit:4.13.2")
}
