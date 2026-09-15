/*
 * The e_sqlite3 provider in SQLitePCLRaw imports the SQLCipher key functions
 * even from a plain SQLite build, so the packaged library ships these stubs,
 * which fail with SQLITE_ERROR. Same here, so the library we build for Linux
 * exports exactly what the provider expects.
 */
#ifndef SQLITE_API
#define SQLITE_API
#endif

SQLITE_API int sqlite3_key(void *db, const void *pKey, int nKey)
{
    (void)db; (void)pKey; (void)nKey;
    return 1;
}

SQLITE_API int sqlite3_rekey(void *db, const void *pKey, int nKey)
{
    (void)db; (void)pKey; (void)nKey;
    return 1;
}

SQLITE_API int sqlite3_key_v2(void *db, const char *zDbName, const void *pKey, int nKey)
{
    (void)db; (void)zDbName; (void)pKey; (void)nKey;
    return 1;
}

SQLITE_API int sqlite3_rekey_v2(void *db, const char *zDbName, const void *pKey, int nKey)
{
    (void)db; (void)zDbName; (void)pKey; (void)nKey;
    return 1;
}
