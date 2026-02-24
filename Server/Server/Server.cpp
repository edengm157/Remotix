#include "Server.h"

Server::Server()
    : m_handlerFactory()
    , m_communicator(m_handlerFactory)
    , m_database(new SqliteDataBase())
{
    // Database is opened by LoginManager in RequestHandlerFactory
    // No need to open it here
}

Server::~Server()
{
    if (m_database != nullptr)
    {
        m_database->close();
        delete m_database;
        m_database = nullptr;
    }
}

void Server::run()
{
    std::cout << "======================================" << std::endl;
    std::cout << "  Screen Sharing Connection Server" << std::endl;
    std::cout << "======================================" << std::endl;
    std::cout << "Port: 12345" << std::endl;
    std::cout << "Database: ScreenShare.sqlite" << std::endl;
    std::cout << "Room IDs: 0001-9999" << std::endl;
    std::cout << "======================================" << std::endl << std::endl;

    // Start the communicator in a separate thread
    std::thread t_connector(&Communicator::startHandleRequests, &m_communicator);
    t_connector.detach();

    std::cout << "Server started successfully!" << std::endl;
    std::cout << "Type 'EXIT' to shut down the server." << std::endl << std::endl;

    // Command loop
    std::string input;
    while (true)
    {
        std::cin >> input;

        if (input == "EXIT" || input == "exit")
        {
            std::cout << "Shutting down server..." << std::endl;
            std::exit(EXIT_SUCCESS);
        }
        else
        {
            std::cout << "Unknown command. Available commands:" << std::endl;
            std::cout << "  EXIT - Shut down the server" << std::endl;
        }
    }
}