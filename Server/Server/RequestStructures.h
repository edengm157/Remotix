#pragma once
#include <string>

// Authentication requests
typedef struct {
    std::string username;
    std::string password;
} LoginRequest;

typedef struct {
    std::string username;
    std::string password;
} SignupRequest;

// Room requests
typedef struct {
    unsigned int roomId;
} JoinRoomRequest;

typedef struct {
    unsigned int roomId;
} LeaveRoomRequest;