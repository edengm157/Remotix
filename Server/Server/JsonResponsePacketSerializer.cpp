#include "JsonResponsePacketSerializer.h"

using json = nlohmann::json;

buffer JsonResponsePacketSerializer::serializeResponse(const ErrorResponse& response)
{
	buffer result;
	result.reserve(SIZE_OF_HEADER);

	// Convert the ErrorResponse to JSON
	json j = {
		{"message", response.message}
	};

	// Serialize the JSON to a string
	std::string jsonString = j.dump();

	// Insert the code & size of json at the beginning of the buffer
	result.push_back(static_cast<char>(0));  // Error code = 0
	addSizeToVector(result, jsonString.size());

	// Convert the string to a buffer
	result.resize(jsonString.size() + SIZE_OF_HEADER);
	std::copy(jsonString.begin(), jsonString.end(), result.begin() + SIZE_OF_HEADER);

	return result;
}

buffer JsonResponsePacketSerializer::serializeResponse(const LoginResponse& response)
{
	buffer result;
	result.reserve(SIZE_OF_HEADER);

	// Convert the LoginResponse to JSON
	json j = {
		{"status", response.status}
	};

	// Serialize the JSON to a string
	std::string jsonString = j.dump();

	// Insert the code & size of json at the beginning of the buffer
	result.push_back(static_cast<char>(1));  // Success code = 1
	addSizeToVector(result, jsonString.size());

	// Convert the string to a buffer
	result.resize(jsonString.size() + SIZE_OF_HEADER);
	std::copy(jsonString.begin(), jsonString.end(), result.begin() + SIZE_OF_HEADER);

	return result;
}

buffer JsonResponsePacketSerializer::serializeResponse(const SignupResponse& response)
{
	buffer result;
	result.reserve(SIZE_OF_HEADER);

	// Convert the SignupResponse to JSON
	json j = {
		{"status", response.status}
	};

	// Serialize the JSON to a string
	std::string jsonString = j.dump();

	// Insert the code & size of json at the beginning of the buffer
	result.push_back(static_cast<uint8_t>(1));  // Success code = 1
	addSizeToVector(result, static_cast<uint32_t>(jsonString.size()));

	// Convert the string to a buffer
	result.resize(jsonString.size() + SIZE_OF_HEADER);
	std::copy(jsonString.begin(), jsonString.end(), result.begin() + SIZE_OF_HEADER);

	return result;
}

buffer JsonResponsePacketSerializer::serializeResponse(const LogoutResponse& response)
{
	buffer result;
	result.reserve(SIZE_OF_HEADER);

	// Convert the LogoutResponse to JSON
	json j = {
		{"status", response.status}
	};

	// Serialize the JSON to a string
	std::string jsonString = j.dump();

	// Insert the code & size of json at the beginning of the buffer
	result.push_back(static_cast<uint8_t>(1));  // Success code = 1
	addSizeToVector(result, static_cast<uint32_t>(jsonString.size()));

	// Convert the string to a buffer
	result.resize(jsonString.size() + SIZE_OF_HEADER);
	std::copy(jsonString.begin(), jsonString.end(), result.begin() + SIZE_OF_HEADER);

	return result;
}

buffer JsonResponsePacketSerializer::serializeResponse(const CreateRoomResponse& response)
{
	buffer result;
	result.reserve(SIZE_OF_HEADER);

	// Convert the CreateRoomResponse to JSON
	// IMPORTANT: Now includes roomId!
	json j = {
		{"status", response.status},
		{"roomId", response.roomId}  // <-- NEW FIELD
	};

	// Serialize the JSON to a string
	std::string jsonString = j.dump();

	// Insert the code & size of json at the beginning of the buffer
	result.push_back(static_cast<uint8_t>(1));  // Success code = 1
	addSizeToVector(result, static_cast<uint32_t>(jsonString.size()));

	// Convert the string to a buffer
	result.resize(jsonString.size() + SIZE_OF_HEADER);
	std::copy(jsonString.begin(), jsonString.end(), result.begin() + SIZE_OF_HEADER);

	return result;
}

buffer JsonResponsePacketSerializer::serializeResponse(const JoinRoomResponse& response)
{
	buffer result;
	result.reserve(SIZE_OF_HEADER);

	// Convert the JoinRoomResponse to JSON
	json j = {
		{"status", response.status}
	};

	// Serialize the JSON to a string
	std::string jsonString = j.dump();

	// Insert the code & size of json at the beginning of the buffer
	result.push_back(static_cast<uint8_t>(1));  // Success code = 1
	addSizeToVector(result, static_cast<uint32_t>(jsonString.size()));

	// Convert the string to a buffer
	result.resize(jsonString.size() + SIZE_OF_HEADER);
	std::copy(jsonString.begin(), jsonString.end(), result.begin() + SIZE_OF_HEADER);

	return result;
}

buffer JsonResponsePacketSerializer::serializeResponse(const LeaveRoomResponse& response)
{
	buffer result;
	result.reserve(SIZE_OF_HEADER);

	// Convert the LeaveRoomResponse to JSON
	json j = {
		{"status", response.status}
	};

	// Serialize the JSON to a string
	std::string jsonString = j.dump();

	// Insert the code & size of json at the beginning of the buffer
	result.push_back(static_cast<uint8_t>(1));  // Success code = 1
	addSizeToVector(result, static_cast<uint32_t>(jsonString.size()));

	// Convert the string to a buffer
	result.resize(jsonString.size() + SIZE_OF_HEADER);
	std::copy(jsonString.begin(), jsonString.end(), result.begin() + SIZE_OF_HEADER);

	return result;
}

void JsonResponsePacketSerializer::addSizeToVector(std::vector<char>& vec, uint32_t size)
{
	// Break the 4-byte integer into individual bytes (big-endian)
	vec.push_back(static_cast<char>((size >> 24) & 0xFF)); // Most significant byte
	vec.push_back(static_cast<char>((size >> 16) & 0xFF));
	vec.push_back(static_cast<char>((size >> 8) & 0xFF));
	vec.push_back(static_cast<char>(size & 0xFF)); // Least significant byte
}