#pragma once
#include "RequestStructures.h"
#include <string>
#include "json.hpp"
#include "json_fwd.hpp"

typedef std::vector<char> buffer;

class JsonRequestDeserializer
{
public:
	/*
	* This function deserializes a JSON string into a LoginRequest object.
	* It expects the JSON string to contain "username" and "password" fields.
	*
	* @param recvBuffer - The buffer containing the JSON string.
	* @return request - The deserialized LoginRequest object.
	*/
	static LoginRequest deserializeLoginRequest(const buffer& recvBuffer);

	/*
	* This function deserializes a JSON string into a SignupRequest object.
	* It expects the JSON string to contain "username" and "password" fields.
	* NOTE: Email field has been removed from the simplified server.
	*
	* @param recvBuffer - The buffer containing the JSON string.
	* @return request - The deserialized SignupRequest object.
	*/
	static SignupRequest deserializeSignupRequest(const buffer& recvBuffer);

	/*
	* This function deserializes a JSON string into a JoinRoomRequest object.
	* It expects the JSON string to contain "roomId" field.
	*
	* @param recvBuffer - The buffer containing the JSON string.
	* @return request - The deserialized JoinRoomRequest object.
	*/
	static JoinRoomRequest deserializeJoinRoomRequest(const buffer& recvBuffer);

	/*
	* This function deserializes a JSON string into a LeaveRoomRequest object.
	* It expects the JSON string to contain "roomId" field (optional - handler knows which room).
	*
	* @param recvBuffer - The buffer containing the JSON string.
	* @return request - The deserialized LeaveRoomRequest object.
	*/
	static LeaveRoomRequest deserializeLeaveRoomRequest(const buffer& recvBuffer);

private:
	//definitions:
	constexpr static unsigned int SIZE_OF_HEADER = 5;
};