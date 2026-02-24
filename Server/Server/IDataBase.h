#pragma once
#include <string>

class IDataBase
{
public:
    virtual ~IDataBase() = default;

    virtual bool open() = 0;
    virtual void close() = 0;

    virtual bool doesUserExists(const std::string& username) = 0;
    virtual bool doesPasswordMatch(const std::string& username, const std::string& password) = 0;
    virtual void addNewUser(const std::string& username, const std::string& password) = 0;
};