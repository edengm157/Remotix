#include <iostream>
#include "WSAInitializer.h"
#include "Server.h"

int main()
{
    try
    {
        // Initialize Winsock
        WSAInitializer wsa_init;

        // Create and run the server
        Server server;
        server.run();
    }
    catch (std::exception& e)
    {
        std::cerr << "Fatal error: " << e.what() << std::endl;
        return 1;
    }
    catch (...)
    {
        std::cerr << "Unknown fatal error occurred!" << std::endl;
        return 1;
    }

    return 0;
}