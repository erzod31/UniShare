package org.unishare.app

object MembershipRemoval {
    fun merge(localRemovedAt: String?, remoteRemovedAt: String?): String? =
        localRemovedAt ?: remoteRemovedAt
}
