#pragma once

// Request codes for client-server communication
enum class RequestCodes : char
{
    // Authentication
    LOGIN = 1,
    SIGNUP = 2,

    // Menu operations (only these 3)
    LOGOUT = 3,
    CREATE_ROOM = 4,
    JOIN_ROOM = 5,

    // Room operations (when inside a room)
    LEAVE_ROOM = 6
    // Future: STREAM_FRAME = 7, etc.
};

// Response codes
enum class ResponseCodes : char
{
    SUCCESS = 1,
    NOT_SUCCESSFUL = 0
};

// Room status
enum class RoomStatus : unsigned int
{
    WAITING_FOR_USERS,
    ACTIVE,
    CLOSED
};