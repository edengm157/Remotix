#pragma once
#include <string>
#include <vector>
#include "Room.h"

// Error response
typedef struct {
    std::string message;
} ErrorResponse;

// Authentication responses
typedef struct {
    unsigned int status;
} LoginResponse;

typedef struct {
    unsigned int status;
} SignupResponse;

typedef struct {
    unsigned int status;
} LogoutResponse;

// Room responses
typedef struct {
    unsigned int status;
    unsigned int roomId;  // The newly created room ID
} CreateRoomResponse;

typedef struct {
    unsigned int status;
} JoinRoomResponse;

typedef struct {
    unsigned int status;
} LeaveRoomResponse;