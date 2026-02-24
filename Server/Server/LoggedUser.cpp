#include "LoggedUser.h"

LoggedUser::LoggedUser(const std::string& username)
    : m_username(username)
{
}

LoggedUser::~LoggedUser()
{
}

std::string LoggedUser::getUsername() const
{
    return m_username;
}

bool LoggedUser::operator==(const LoggedUser& other) const
{
    return m_username == other.m_username;
}