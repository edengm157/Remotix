#include "JsonRequestDeserializer.h"

using json = nlohmann::json;

LoginRequest JsonRequestDeserializer::deserializeLoginRequest(const buffer& recvBuffer)
{
	// Extract the JSON string from the buffer (skip 5-byte header)
	std::string jsonString(recvBuffer.begin() + SIZE_OF_HEADER, recvBuffer.end());
	json j = json::parse(jsonString);

	// Deserialize the JSON into a LoginRequest object
	LoginRequest request;
	request.username = j["username"].get<std::string>();
	request.password = j["password"].get<std::string>();

	return request;
}

SignupRequest JsonRequestDeserializer::deserializeSignupRequest(const buffer& recvBuffer)
{
	// Extract the JSON string from the buffer (skip 5-byte header)
	std::string jsonString(recvBuffer.begin() + SIZE_OF_HEADER, recvBuffer.end());
	json j = json::parse(jsonString);

	// Deserialize the JSON into a SignupRequest object
	// NOTE: Email field removed - only username and password now
	SignupRequest request;
	request.username = j["username"].get<std::string>();
	request.password = j["password"].get<std::string>();

	return request;
}

JoinRoomRequest JsonRequestDeserializer::deserializeJoinRoomRequest(const buffer& recvBuffer)
{
	// Extract the JSON string from the buffer (skip 5-byte header)
	std::string jsonString(recvBuffer.begin() + SIZE_OF_HEADER, recvBuffer.end());
	json j = json::parse(jsonString);

	// Deserialize the JSON into a JoinRoomRequest object
	JoinRoomRequest request;
	request.roomId = j["roomId"].get<unsigned int>();

	return request;
}

LeaveRoomRequest JsonRequestDeserializer::deserializeLeaveRoomRequest(const buffer& recvBuffer)
{
	// Extract the JSON string from the buffer (skip 5-byte header)
	std::string jsonString(recvBuffer.begin() + SIZE_OF_HEADER, recvBuffer.end());

	// Create request
	LeaveRoomRequest request;

	// Parse JSON if present (might be empty for LEAVE_ROOM)
	if (!jsonString.empty() && jsonString != "{}")
	{
		json j = json::parse(jsonString);
		request.roomId = j["roomId"].get<unsigned int>();
	}
	else
	{
		// If no roomId provided, set to 0 (handler will use its stored room ID)
		request.roomId = 0;
	}

	return request;
}