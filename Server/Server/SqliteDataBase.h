#pragma once
#include "IDataBase.h"
#include "sqlite3.h"
#include <io.h>

class SqliteDataBase : public IDataBase
{
public:
    SqliteDataBase() = default;
    virtual ~SqliteDataBase() = default;

    bool open() override;
    void close() override;

    // User operations
    bool doesUserExists(const std::string& username) override;
    bool doesPasswordMatch(const std::string& username, const std::string& password) override;
    void addNewUser(const std::string& username, const std::string& password) override;

private:
    sqlite3* m_db = nullptr;
    const char* m_dbFileName = "SqlDb.db";

    void sqlExecuteWrapper(std::string sqlStatement);
    void sqlExecuteCallbackWrapper(std::string sqlStatement, int(*callback)(void*, int, char**, char**), void* data);
    static int usersCallback(void* data, int argc, char** argv, char** azColName);
};