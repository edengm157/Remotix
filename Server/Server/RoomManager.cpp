#include "RoomManager.h"
#include <chrono>

RoomManager::RoomManager()
{
    // Seed the random generator with current time
    auto seed = std::chrono::system_clock::now().time_since_epoch().count();
    m_randomGenerator.seed(static_cast<unsigned int>(seed));
}

unsigned int RoomManager::generateUniqueRoomId()
{
    std::uniform_int_distribution<unsigned int> distribution(1, 9999);

    unsigned int roomId;
    int attempts = 0;
    const int MAX_ATTEMPTS = 10000; // Prevent infinite loop

    do
    {
        roomId = distribution(m_randomGenerator);
        attempts++;

        if (attempts >= MAX_ATTEMPTS)
        {
            throw RoomException("Unable to generate unique room ID - all IDs may be in use");
        }
    } while (roomExists(roomId));

    return roomId;
}

unsigned int RoomManager::createRoom(const LoggedUser& creator)
{
    unsigned int roomId = generateUniqueRoomId();

    RoomData roomData;
    roomData.id = roomId;
    roomData.creatorUsername = creator.getUsername();
    roomData.status = RoomStatus::WAITING_FOR_USERS;

    m_rooms.emplace(roomId, Room(roomData));
    m_rooms.at(roomId).addUser(creator);

    return roomId;
}

void RoomManager::deleteRoom(unsigned int id)
{
    auto it = m_rooms.find(id);
    if (it == m_rooms.end())
    {
        throw RoomException("A room with this ID doesn't exist", id);
    }
    m_rooms.erase(it);
}

Room& RoomManager::getRoom(unsigned int id)
{
    auto it = m_rooms.find(id);
    if (it == m_rooms.end())
    {
        throw RoomException("A room with this ID doesn't exist", id);
    }
    return it->second;
}

std::vector<RoomData> RoomManager::getRooms() const
{
    std::vector<RoomData> rooms;
    for (const auto& pair : m_rooms)
    {
        rooms.push_back(pair.second.getRoomData());
    }
    return rooms;
}

bool RoomManager::roomExists(unsigned int id) const
{
    return m_rooms.find(id) != m_rooms.end();
}