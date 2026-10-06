package org.unishare.app

enum class SyncMergeAction { IMPORT, UNCHANGED, APPLY_REMOTE, KEEP_LOCAL, CONFLICT }

data class SyncBaseline(val localHash: String, val remoteHash: String)

object SyncMerge {
    fun decide(
        localHash: String?,
        remoteHash: String,
        baseline: SyncBaseline?,
        unresolvedConflict: Boolean,
    ): SyncMergeAction {
        if (localHash == null) return SyncMergeAction.IMPORT
        if (localHash == remoteHash) return SyncMergeAction.UNCHANGED
        if (unresolvedConflict || baseline == null) return SyncMergeAction.CONFLICT
        val localChanged = localHash != baseline.localHash
        val remoteChanged = remoteHash != baseline.remoteHash
        return when {
            !localChanged && remoteChanged -> SyncMergeAction.APPLY_REMOTE
            localChanged && !remoteChanged -> SyncMergeAction.KEEP_LOCAL
            else -> SyncMergeAction.CONFLICT
        }
    }
}
