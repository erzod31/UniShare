package org.unishare.app

import android.content.Context
import android.content.ContextWrapper
import android.content.SharedPreferences
import android.database.DatabaseErrorHandler
import android.database.sqlite.SQLiteDatabase
import java.io.File
import java.util.UUID

/** Every test gets a separate database, CAS, cache and pairing state, even on a real device. */
internal class IsolatedLibraryContext(base: Context) : ContextWrapper(base), AutoCloseable {
    private val prefix = "qa-${UUID.randomUUID()}-"
    private val preferences = mutableSetOf<String>()
    private val testFiles = File(base.filesDir, prefix).apply { mkdirs() }
    private val testCache = File(base.cacheDir, prefix).apply { mkdirs() }

    override fun getApplicationContext(): Context = this
    override fun getFilesDir(): File = testFiles
    override fun getCacheDir(): File = testCache
    override fun getDatabasePath(name: String): File = super.getDatabasePath(prefix + name)
    override fun openOrCreateDatabase(
        name: String, mode: Int, factory: SQLiteDatabase.CursorFactory?,
    ): SQLiteDatabase = super.openOrCreateDatabase(prefix + name, mode, factory)

    override fun openOrCreateDatabase(
        name: String, mode: Int, factory: SQLiteDatabase.CursorFactory?, handler: DatabaseErrorHandler?,
    ): SQLiteDatabase = super.openOrCreateDatabase(prefix + name, mode, factory, handler)

    override fun getSharedPreferences(name: String, mode: Int): SharedPreferences {
        preferences += prefix + name
        return super.getSharedPreferences(prefix + name, mode)
    }

    override fun close() {
        super.deleteDatabase(prefix + "unishare.db")
        preferences.forEach { super.deleteSharedPreferences(it) }
        testFiles.deleteRecursively()
        testCache.deleteRecursively()
    }
}
