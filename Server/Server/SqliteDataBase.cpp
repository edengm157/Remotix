#include "SqliteDataBase.h"
#include <iostream>
#include <list>

void SqliteDataBase::sqlExecuteWrapper(std::string sqlStatement)
{
    char* errorMessage;
    int databaseStatus = sqlite3_exec(m_db, sqlStatement.c_str(), nullptr, nullptr, &errorMessage);

    if (databaseStatus != SQLITE_OK)
    {
        std::cerr << "SQL Error: " << errorMessage << std::endl;
        sqlite3_free(errorMessage);
    }
}

void SqliteDataBase::sqlExecuteCallbackWrapper(std::string sqlStatement, int(*callback)(void*, int, char**, char**), void* data)
{
    char* errorMessage;
    int databaseStatus = sqlite3_exec(m_db, sqlStatement.c_str(), callback, data, &errorMessage);

    if (databaseStatus != SQLITE_OK)
    {
        std::cerr << "SQL Error: " << errorMessage << std::endl;
        sqlite3_free(errorMessage);
    }
}

int SqliteDataBase::usersCallback(void* data, int argc, char** argv, char** azColName)
{
    std::list<std::list<std::string>>* users = static_cast<std::list<std::list<std::string>>*>(data);
    std::list<std::string> user;

    // Store username and password
    if (argc >= 2)
    {
        user.push_back(argv[1]); // username
        user.push_back(argv[2]); // password
    }

    users->push_back(user);
    return 0;
}

bool SqliteDataBase::open()
{
    // Check if database file exists
    int fileStatus = _access(m_dbFileName, 0);
    int databaseStatus = sqlite3_open(m_dbFileName, &m_db);

    if (databaseStatus != SQLITE_OK)
    {
        m_db = nullptr;
        std::cerr << "Failed to open/create database" << std::endl;
        return false;
    }

    // If file didn't exist, create the tables
    if (fileStatus != 0)
    {
        std::cout << "Creating new database..." << std::endl;

        char* errorMessage = nullptr;

        // Create simplified Users table (no email, no statistics)
        std::string sqlStatement =
            "CREATE TABLE Users ("
            "  id INTEGER PRIMARY KEY AUTOINCREMENT, "
            "  username TEXT NOT NULL UNIQUE, "
            "  password TEXT NOT NULL"
            ");";

        databaseStatus = sqlite3_exec(m_db, sqlStatement.c_str(), nullptr, nullptr, &errorMessage);

        if (databaseStatus != SQLITE_OK)
        {
            std::cerr << "Error creating tables: " << errorMessage << std::endl;
            sqlite3_free(errorMessage);
            return false;
        }

        std::cout << "Database created successfully." << std::endl;
    }

    return true;
}

void SqliteDataBase::close()
{
    if (m_db != nullptr)
    {
        sqlite3_close(m_db);
        m_db = nullptr;
    }
}

bool SqliteDataBase::doesUserExists(const std::string& username)
{
    std::string sqlStatement = "SELECT * FROM Users WHERE username = '" + username + "';";
    std::list<std::list<std::string>> users;
    sqlExecuteCallbackWrapper(sqlStatement, SqliteDataBase::usersCallback, &users);

    return !users.empty();
}

bool SqliteDataBase::doesPasswordMatch(const std::string& username, const std::string& password)
{
    std::string sqlStatement = "SELECT * FROM Users WHERE username = '" + username + "' AND password = '" + password + "';";
    std::list<std::list<std::string>> users;
    sqlExecuteCallbackWrapper(sqlStatement, SqliteDataBase::usersCallback, &users);

    return !users.empty();
}

void SqliteDataBase::addNewUser(const std::string& username, const std::string& password)
{
    std::string sqlStatement = "INSERT INTO Users (username, password) VALUES('" + username + "', '" + password + "');";
    sqlExecuteWrapper(sqlStatement);
}