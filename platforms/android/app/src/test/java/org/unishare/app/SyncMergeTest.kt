package org.unishare.app

import org.junit.Assert.assertEquals
import org.junit.Test

class SyncMergeTest {
    @Test fun importsMissingItems() = assertEquals(
        SyncMergeAction.IMPORT, SyncMerge.decide(null, "remote", null, false),
    )

    @Test fun recognizesIdenticalItems() = assertEquals(
        SyncMergeAction.UNCHANGED, SyncMerge.decide("same", "same", null, false),
    )

    @Test fun appliesOnlyRemoteChange() = assertEquals(
        SyncMergeAction.APPLY_REMOTE,
        SyncMerge.decide("base-local", "new-remote", SyncBaseline("base-local", "base-remote"), false),
    )

    @Test fun keepsOnlyLocalChange() = assertEquals(
        SyncMergeAction.KEEP_LOCAL,
        SyncMerge.decide("new-local", "base-remote", SyncBaseline("base-local", "base-remote"), false),
    )

    @Test fun preservesConcurrentChangesAsConflict() = assertEquals(
        SyncMergeAction.CONFLICT,
        SyncMerge.decide("new-local", "new-remote", SyncBaseline("base-local", "base-remote"), false),
    )

    @Test fun unresolvedConflictCannotBeOverwritten() = assertEquals(
        SyncMergeAction.CONFLICT,
        SyncMerge.decide("local", "remote", SyncBaseline("local", "old"), true),
    )
}
