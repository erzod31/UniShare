package org.unishare.app

import org.junit.Assert.assertEquals
import org.junit.Test

class ItemBatchSelectionTest {
    @Test
    fun `toggle adds and removes stable ids`() {
        assertEquals(setOf("a", "b"), ItemBatchSelection.toggle(setOf("a"), "b"))
        assertEquals(setOf("b"), ItemBatchSelection.toggle(setOf("a", "b"), "a"))
    }

    @Test
    fun `batch actions partition active and archived items`() {
        val active = item("active")
        val archived = item("archived", deletedAt = "2026-10-06T12:00:00Z")
        val unselected = item("other")
        val selected = setOf(active.id, archived.id)

        assertEquals(listOf("active"), ItemBatchSelection.eligibleIds(listOf(active, archived, unselected), selected, true))
        assertEquals(listOf("archived"), ItemBatchSelection.eligibleIds(listOf(active, archived, unselected), selected, false))
        assertEquals(selected, ItemBatchSelection.retainVisible(selected + "missing", listOf(active, archived)))
    }

    private fun item(id: String, deletedAt: String? = null) = SavedItem(
        id = id,
        kind = 1,
        title = id,
        originalUrl = "https://example.com/$id",
        createdAt = "2026-10-06T12:00:00Z",
        assetSha256 = null,
        deletedAt = deletedAt,
    )
}
