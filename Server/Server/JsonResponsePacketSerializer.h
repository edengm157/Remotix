#pragma once
#include "json.hpp"
#include "json_fwd.hpp"
#include "ResponseStructures.h"

typedef std::vector<char> buffer;

class JsonResponsePacketSerializer
{
public:
	/*
	* This function serializes the ErrorResponse to a JSON string and returns it as a buffer.
	* The first byte of the buffer indicates the type of response (0 for ErrorResponse).
	* The next 4 bytes indicate the size of the JSON string (big-endian).
	* The rest of the buffer contains the serialized JSON string.
	*
	* @param response - The ErrorResponse object to be serialized.
	* @return buffer - The serialized JSON string as a buffer.
	*/
	static buffer serializeResponse(const ErrorResponse&);

	/*
	* This function serializes the LoginResponse to a JSON string and returns it as a buffer.
	* The first byte of the buffer indicates the type of response (1 for LoginResponse).
	* The next 4 bytes indicate the size of the JSON string (big-endian).
	* The rest of the buffer contains the serialized JSON string.
	*
	* @param response - The LoginResponse object to be serialized.
	* @return buffer - The serialized JSON string as a buffer.
	*/
	static buffer serializeResponse(const LoginResponse&);

	/*
	* This function serializes the SignupResponse to a JSON string and returns it as a buffer.
	* The first byte of the buffer indicates the type of response (1 for SignupResponse).
	* The next 4 bytes indicate the size of the JSON string (big-endian).
	* The rest of the buffer contains the serialized JSON string.
	*
	* @param response - The SignupResponse object to be serialized.
	* @return buffer - The serialized JSON string as a buffer.
	*/
	static buffer serializeResponse(const SignupResponse&);

	/*
	* This function serializes the LogoutResponse to a JSON string and returns it as a buffer.
	* The first byte of the buffer indicates the type of response (1 for LogoutResponse).
	* The next 4 bytes indicate the size of the JSON string (big-endian).
	* The rest of the buffer contains the serialized JSON string.
	*
	* @param response - The LogoutResponse object to be serialized.
	* @return buffer - The serialized JSON string as a buffer.
	*/
	static buffer serializeResponse(const LogoutResponse&);

	/*
	* This function serializes the CreateRoomResponse to a JSON string and returns it as a buffer.
	* The first byte of the buffer indicates the type of response (1 for CreateRoomResponse).
	* The next 4 bytes indicate the size of the JSON string (big-endian).
	* The rest of the buffer contains the serialized JSON string.
	*
	* IMPORTANT: This now includes the roomId field in the JSON!
	* JSON format: { "status": 1, "roomId": 1234 }
	*
	* @param response - The CreateRoomResponse object to be serialized.
	* @return buffer - The serialized JSON string as a buffer.
	*/
	static buffer serializeResponse(const CreateRoomResponse&);

	/*
	* This function serializes the JoinRoomResponse to a JSON string and returns it as a buffer.
	* The first byte of the buffer indicates the type of response (1 for JoinRoomResponse).
	* The next 4 bytes indicate the size of the JSON string (big-endian).
	* The rest of the buffer contains the serialized JSON string.
	*
	* @param response - The JoinRoomResponse object to be serialized.
	* @return buffer - The serialized JSON string as a buffer.
	*/
	static buffer serializeResponse(const JoinRoomResponse&);

	/*
	* This function serializes the LeaveRoomResponse to a JSON string and returns it as a buffer.
	* The first byte of the buffer indicates the type of response (1 for LeaveRoomResponse).
	* The next 4 bytes indicate the size of the JSON string (big-endian).
	* The rest of the buffer contains the serialized JSON string.
	*
	* @param response - The LeaveRoomResponse object to be serialized.
	* @return buffer - The serialized JSON string as a buffer.
	*/
	static buffer serializeResponse(const LeaveRoomResponse&);

private:
	//helper function:
	/*
	* This function adds the size of the message to the beginning of the buffer.
	* It breaks the 4-byte integer into individual bytes and appends them to the vector.
	* The first byte is the most significant byte and the last byte is the least significant byte.
	*
	* @param vec - The vector to which the size will be added.
	* @param size - The size of the message to be added.
	*/
	static void addSizeToVector(std::vector<char>& vec, uint32_t size);

	//definitions:
	static constexpr unsigned int SIZE_OF_HEADER = 5;
};