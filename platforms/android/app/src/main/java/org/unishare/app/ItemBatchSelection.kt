package org.unishare.app

internal object ItemBatchSelection {
    fun toggle(selectedIds: Set<String>, itemId: String): Set<String> =
        if (itemId in selectedIds) selectedIds - itemId else selectedIds + itemId

    fun retainVisible(selectedIds: Set<String>, items: Collection<SavedItem>): Set<String> {
        val visibleIds = items.mapTo(mutableSetOf()) { it.id }
        return selectedIds.intersect(visibleIds)
    }

    fun eligibleIds(items: Collection<SavedItem>, selectedIds: Set<String>, archive: Boolean): List<String> =
        items
            .filter { it.id in selectedIds && (if (archive) it.deletedAt == null else it.deletedAt != null) }
            .map { it.id }
}
