#pragma once
#include <string>

class LoggedUser
{
public:
    LoggedUser(const std::string& username);
    ~LoggedUser();

    std::string getUsername() const;
    bool operator==(const LoggedUser& other) const;

private:
    std::string m_username;
};