package org.unishare.app

import java.nio.charset.StandardCharsets
import java.util.UUID

/** Resolves the peer identity used while merging portable packages. */
object PortablePackageIdentity {
    private val legacySourceDeviceId = UUID.nameUUIDFromBytes(
        "UniShare portable v1 package without source_device_id".toByteArray(StandardCharsets.UTF_8),
    ).toString()

    fun sourceDeviceId(explicitValue: String?): String {
        val explicit = explicitValue?.trim().orEmpty()
        return if (explicit.isEmpty()) legacySourceDeviceId else UUID.fromString(explicit).toString()
    }
}
