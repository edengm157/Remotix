#pragma once
#include <map>
#include <random>
#include <stdexcept>
#include "Room.h"

class RoomException : public std::runtime_error
{
public:
    RoomException(const std::string& message, unsigned int roomId = 0)
        : std::runtime_error(message), m_roomId(roomId) {}

    unsigned int getRoomId() const { return m_roomId; }

private:
    unsigned int m_roomId;
};

class RoomManager
{
public:
    RoomManager();

    // Create a room with random 4-digit ID (0001-9999)
    unsigned int createRoom(const LoggedUser& creator);

    // Delete a room by ID
    void deleteRoom(unsigned int id);

    // Get room by ID
    Room& getRoom(unsigned int id);

    // Get all rooms
    std::vector<RoomData> getRooms() const;

    // Check if a room exists
    bool roomExists(unsigned int id) const;

private:
    std::map<unsigned int, Room> m_rooms;
    std::mt19937 m_randomGenerator;

    // Generate a unique random 4-digit room ID
    unsigned int generateUniqueRoomId();
};