#pragma once
#include <iostream>
#include <string>
#include <iomanip>
#include <sstream>
#include <mutex>
#include <thread>
#include <map>
#include <WinSock2.h>
#include "LoginRequestHandler.h"
#include "Protocol.h"

class Communicator
{
public:
    Communicator(RequestHandlerFactory& factory);
    ~Communicator();

    void startHandleRequests();

private:
    typedef unsigned int uint;

    constexpr static unsigned short PORT = 12345;
    constexpr static uint IFACE = 0;
    static constexpr unsigned int SIZE_OF_HEADER = 5;

    SOCKET m_serverSocket;
    std::map<SOCKET, IRequestHandler*> m_clients;
    std::mutex m_ClientsMutex;
    RequestHandlerFactory& m_handlerFactory;

    void handleNewClient(const SOCKET clientSocket);
    void bindAndListen();
    std::string timeConvertion(std::time_t t);
};