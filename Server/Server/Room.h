#pragma once
#include <string>
#include <vector>
#include "LoggedUser.h"
#include "Protocol.h"

// Simplified room data structure for screen sharing
typedef struct
{
    unsigned int id;              // 4-digit room ID (0001-9999)
    std::string creatorUsername;  // The screen sharer
    RoomStatus status;            // Room status
} RoomData;

class Room
{
public:
    Room(const RoomData& roomData);

    void addUser(const LoggedUser& user);
    void removeUser(const LoggedUser& user);
    std::vector<std::string> getAllUsers() const;
    std::string getCreator() const;

    RoomData getRoomData() const;
    void setRoomStatus(RoomStatus status);

private:
    RoomData m_metadata;
    std::vector<LoggedUser> m_users;
};