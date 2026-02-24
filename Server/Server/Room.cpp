#include "Room.h"
#include <algorithm>

Room::Room(const RoomData& roomData)
    : m_metadata(roomData)
{
}

void Room::addUser(const LoggedUser& user)
{
    // Only add if not already in the room
    if (std::find(m_users.begin(), m_users.end(), user) == m_users.end())
    {
        m_users.push_back(user);
    }
}

void Room::removeUser(const LoggedUser& user)
{
    m_users.erase(std::remove(m_users.begin(), m_users.end(), user), m_users.end());
}

std::vector<std::string> Room::getAllUsers() const
{
    std::vector<std::string> usernames;
    for (const auto& user : m_users)
    {
        usernames.push_back(user.getUsername());
    }
    return usernames;
}

std::string Room::getCreator() const
{
    return m_metadata.creatorUsername;
}

RoomData Room::getRoomData() const
{
    return m_metadata;
}

void Room::setRoomStatus(RoomStatus status)
{
    m_metadata.status = status;
}