#pragma once
#include <vector>
#include <algorithm>
#include "IDataBase.h"
#include "LoggedUser.h"

enum class LoginStatus
{
    SUCCESS = 0,
    USER_EXISTS = 1,
    USER_MISSING = 2,
    MISSING_FIELDS = 3,
    WRONG_USER_DETAILS = 4
};

class LoginManager
{
public:
    LoginManager(IDataBase* db);
    ~LoginManager();

    int signup(const std::string& username, const std::string& password);
    int login(const std::string& username, const std::string& password);
    void logout(const std::string& username);

private:
    IDataBase* m_database;
    std::vector<LoggedUser> m_loggedUsers;
};