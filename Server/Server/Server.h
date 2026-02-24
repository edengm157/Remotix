#pragma once
#include "Communicator.h"
#include "RequestHandlerFactory.h"
#include "SqliteDataBase.h"
#include <thread>
#include <iostream>
#include <string>

class Server
{
public:
    Server();
    ~Server();

    void run();

private:
    RequestHandlerFactory m_handlerFactory;
    Communicator m_communicator;
    IDataBase* m_database;
};