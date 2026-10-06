package org.unishare.app

import java.text.Normalizer
import java.util.Locale

data class OrganizationName(val display: String, val normalized: String)

object OrganizationNames {
    fun normalize(raw: String): OrganizationName {
        val display = Normalizer.normalize(raw, Normalizer.Form.NFKC).trim()
            .split(Regex("\\s+")).filter { it.isNotEmpty() }.joinToString(" ")
        require(display.isNotEmpty() && display.length <= 100) {
            "La etiqueta o colección debe contener entre 1 y 100 caracteres"
        }
        return OrganizationName(display, display.lowercase(Locale.ROOT))
    }

    fun parse(raw: String): List<OrganizationName> = raw
        .split(',')
        .filter { it.isNotBlank() }
        .map(::normalize)
        .distinctBy(OrganizationName::normalized)
}
