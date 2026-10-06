package org.unishare.app

import android.app.Application

class UniShareApplication : Application() {
    val store: LocalLibraryStore by lazy { LocalLibraryStore(applicationContext) }
    val offlineDownloader: OfflinePageDownloader by lazy { OfflinePageDownloader(applicationContext) }
}
