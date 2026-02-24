#include "LoginManager.h"
#include <iostream>

LoginManager::LoginManager(IDataBase* db)
    : m_database(db)
{
    m_database->open();
}

LoginManager::~LoginManager()
{
    if (m_database != nullptr)
    {
        m_database->close();
        delete m_database;
        m_database = nullptr;
    }
    m_loggedUsers.clear();
}

int LoginManager::signup(const std::string& username, const std::string& password)
{
    if (username.empty() || password.empty())
    {
        std::cerr << "Signup failed: Username or password cannot be empty" << std::endl;
        return static_cast<int>(LoginStatus::MISSING_FIELDS);
    }

    if (m_database->doesUserExists(username))
    {
        std::cerr << "Signup failed: User already exists" << std::endl;
        return static_cast<int>(LoginStatus::USER_EXISTS);
    }

    m_database->addNewUser(username, password);
    m_loggedUsers.push_back(LoggedUser(username));

    std::cout << "User '" << username << "' signed up successfully" << std::endl;
    return static_cast<int>(LoginStatus::SUCCESS);
}

int LoginManager::login(const std::string& username, const std::string& password)
{
    if (username.empty() || password.empty())
    {
        std::cerr << "Login failed: Username or password cannot be empty" << std::endl;
        return static_cast<int>(LoginStatus::MISSING_FIELDS);
    }

    if (!m_database->doesUserExists(username))
    {
        std::cerr << "Login failed: User does not exist" << std::endl;
        return static_cast<int>(LoginStatus::USER_MISSING);
    }

    if (!m_database->doesPasswordMatch(username, password))
    {
        std::cerr << "Login failed: Incorrect password" << std::endl;
        return static_cast<int>(LoginStatus::WRONG_USER_DETAILS);
    }

    m_loggedUsers.push_back(LoggedUser(username));

    std::cout << "User '" << username << "' logged in successfully" << std::endl;
    return static_cast<int>(LoginStatus::SUCCESS);
}

void LoginManager::logout(const std::string& username)
{
    m_loggedUsers.erase(
        std::remove_if(
            m_loggedUsers.begin(),
            m_loggedUsers.end(),
            [&username](const LoggedUser& user) {
                return user.getUsername() == username;
            }
        ),
        m_loggedUsers.end()
    );

    std::cout << "User '" << username << "' logged out" << std::endl;
}