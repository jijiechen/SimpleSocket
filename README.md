SimpleSocket
--------

This project provides a lightweight and simple interface for TCP socket programming on the .NET platform, it supports both non-secure and secure connections. In most scenarios, socket programming is painful if you want your application stable and fast. This is partially because the complexity of socket and network stream itself. 
So here it is, this project is for your usage.

If you want to find any patch, please note that this project was extracted from the EventStore code base at revision [6c94358d](https://github.com/EventStore/EventStore/tree/6c94358de88c733814a0f511648d91dfe7909768).

## Usage
Please refer to the sample project to know how to interactive with the code. 
* Before you start, implement your own Framer and then you can start. 
* Instantiate a `TcpService` and handle incomming messages if you want to create a server. You could also build your own mechanism to dispatch different type of messages.
* Instantiate a `TcpConnectionManager` and connect to server if you want to use it as a client.


## Acknowledgements
EventStore is a great project that help build reliable event based systems. This project is fully extracted from EventStore. Credit goes to the EventStore team.

## License
The author of the SimpleSocket project(that is, Jijie Chen) does not add additional restriction on how you can use this project in any forms. 
Howerver, this source code is extracted from the [EventStore](https://github.com/EventStore/EventStore) codebase, use at your own risk, and please follow [the license of EventStore ](https://github.com/EventStore/EventStore/blob/release-v4.0.2/LICENSE.md) too. 

