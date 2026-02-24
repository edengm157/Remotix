#include "Communicator.h"

Communicator::Communicator(RequestHandlerFactory& factory)
    : m_handlerFactory(factory)
{
    m_serverSocket = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (m_serverSocket == INVALID_SOCKET)
        throw std::exception(__FUNCTION__ " - socket");
    std::cout << "Socket created successfully." << std::endl;
}

Communicator::~Communicator()
{
    try
    {
        std::cout << "Closed listening socket." << std::endl;
        ::closesocket(m_serverSocket);

        for (auto client : m_clients)
        {
            std::cout << "Closing client socket..." << std::endl;
            delete client.second;
            ::closesocket(client.first);
            std::cout << "Client socket closed." << std::endl;
        }
    }
    catch (...) {}
}

void Communicator::startHandleRequests()
{
    bindAndListen();
    while (true)
    {
        std::cout << "Accepting client..." << std::endl << std::endl;
        SOCKET clientSocket = ::accept(m_serverSocket, nullptr, nullptr);
        if (clientSocket == INVALID_SOCKET)
        {
            std::cerr << "Failed to accept client." << std::endl;
            continue;
        }
        std::lock_guard<std::mutex> lock(m_ClientsMutex);
        m_clients.emplace(clientSocket, m_handlerFactory.createLoginRequestHandler());
        std::thread clientThread(&Communicator::handleNewClient, this, clientSocket);
        clientThread.detach();
    }
}

void Communicator::bindAndListen()
{
    struct sockaddr_in sa = { 0 };
    sa.sin_port = htons(PORT);
    sa.sin_family = AF_INET;
    sa.sin_addr.s_addr = IFACE;

    if (::bind(m_serverSocket, (struct sockaddr*)&sa, sizeof(sa)) == SOCKET_ERROR)
        throw std::exception(__FUNCTION__ " - bind");
    std::cout << "Binded" << std::endl;

    if (::listen(m_serverSocket, SOMAXCONN) == SOCKET_ERROR)
        throw std::exception(__FUNCTION__ " - listen");
    std::cout << "Listening..." << std::endl;
}

void Communicator::handleNewClient(const SOCKET clientSocket)
{
    std::cout << "Handling new client..." << std::endl;
    RequestResult result;

    while (m_clients[clientSocket] != nullptr)
    {
        try
        {
            buffer recvBuffer(SIZE_OF_HEADER);
            uint32_t bytesReceived = 0;

            // Receiving message in chunks where the first byte is the status and the next 4 bytes are the size of the message:
            bytesReceived = ::recv(clientSocket, recvBuffer.data(), recvBuffer.size(), 0);

            if (bytesReceived == 0 || bytesReceived == SOCKET_ERROR)
            {
                throw std::runtime_error("Client socket closed.");
            }

            // Extracting the size of the message as a binary number:
            uint32_t sizeOfMessage = (static_cast<uint32_t>(recvBuffer[1] & 0xFF) << 24) |
                (static_cast<uint32_t>(recvBuffer[2] & 0xFF) << 16) |
                (static_cast<uint32_t>(recvBuffer[3] & 0xFF) << 8) |
                (static_cast<uint32_t>(recvBuffer[4] & 0xFF));

            recvBuffer.resize(sizeOfMessage + SIZE_OF_HEADER);

            // Receiving the rest of the message:
            while (bytesReceived < SIZE_OF_HEADER + sizeOfMessage)
            {
                int recv_result = ::recv(clientSocket, recvBuffer.data() + bytesReceived, recvBuffer.size() - bytesReceived, 0);
                if (recv_result == SOCKET_ERROR || recv_result == 0)
                {
                    throw std::runtime_error("Client socket closed.");
                }
                bytesReceived += recv_result;
            }

            int status = static_cast<int>(recvBuffer[0]);
            std::cout << "Received status: " << status << std::endl;

            // Creating a requestInfo object:
            RequestInfo requestInfo;
            requestInfo.id = recvBuffer[0];
            requestInfo.receivalTime = std::chrono::system_clock::to_time_t(std::chrono::system_clock::now());
            requestInfo.buffer = recvBuffer;

            //printing output:
            std::cout << "Request size: " << sizeOfMessage << std::endl;
            std::cout << "Request time: " << timeConvertion(requestInfo.receivalTime) << std::endl;
            std::cout << "Handling request..." << std::endl;

            // Handling the request:
            if (m_clients[clientSocket]->isRequestRelevant(requestInfo))
            {
                result = m_clients[clientSocket]->handleRequest(requestInfo);
                std::cout << "Sending response..." << std::endl;

                m_clients[clientSocket] = result.newHandler;
                ::send(clientSocket, result.response.data(), result.response.size(), 0);
            }
            else
            {
                std::cerr << "Invalid request, the client attempted an invalid action." << std::endl;
                ErrorResponse errorResponse;
                errorResponse.message = "Invalid action attempted.";

                buffer responseBuffer = JsonResponsePacketSerializer::serializeResponse(errorResponse);
                ::send(clientSocket, responseBuffer.data(), responseBuffer.size(), 0);
            }
        }
        catch (std::runtime_error& e)
        {
            std::cerr << e.what() << std::endl;
            break;
        }
    }

    std::lock_guard<std::mutex> lock(m_ClientsMutex);
    ::closesocket(clientSocket);
    m_clients.erase(clientSocket);
    std::cout << "Client removed." << std::endl << std::endl;
}

std::string Communicator::timeConvertion(std::time_t t)
{
    std::tm tm_buf;
    localtime_s(&tm_buf, &t);
    std::ostringstream timeStream;
    timeStream << std::put_time(&tm_buf, "%c");
    return timeStream.str();
}