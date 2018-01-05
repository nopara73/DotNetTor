# Tor over TCP (ToT)

## 1. Introduction

### 1.1 Overview

ToT is a simple, application layer, client-server, messaging protocol, that facilitates TCP communication, optimized for Tor's SOCKS5 proxy. ToT defines request-response and subscribe-notify patterns.

### 1.2 Context

HTTP is the most commonly used application layer protcol. However HTTP fingerprinting makes it not ideal for privacy and the subscribe - notify pattern HTTP implementations are hacks.  
Tor is similar to a SOCKS5 proxy that is restricted to TCP. In order to exchange data through well known TCP connections, the connection must be estabilished through the Tor's SOCKS5 proxy first. If the connection is successful, TCP data exchange happens as usual. 

### 1.3 Notation

#### 1.3.1 Keywords

The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY", and "OPTIONAL" in this document are to be interpreted as described in RFC2119.

#### 1.3.2 Packet-format Diagrams

Unless otherwise noted, the decimal numbers appearing in packet-format diagrams represent the length of the corresponding field, in octets. Where a given octet must take on a specific value, the syntax X'hh' is used to denote the value of the single octet in that field.

### 1.4 Requirements

#### 1.4.1 UTF8

ToT uses UTF8 byte encoding, except for its `Content` field. Encoding of the `Content` field is arbitrary, the server and the client must have mutual understanding. When this document specifies the content as string, it means UTF8 encoding.

## 2. Message Format

| Version | MessageType | PurposeLength | Purpose | ContentLength | Content      |
|---------|-------------|---------------|---------|---------------|--------------|
| X'01'   | 1           | 1             | 0-255   | 4             | 0-2147483385 |

### 2.1 MessageType

`X'01'` - `Request`: Issued by the client. A `Response` MUST follow it.  
`X'02'` - `Response`: Issued by the server. A `Request` MUST precede it.  
`X'03'` - `SubscribeRequest`: Issued by the client. A `Response` MUST follow it.  
`X'04'` - `UnsubscribeRequest`: Issued by the client. A `Response` MUST follow it.  
`X'05'` - `Notification`: Issued by the server. It MUST be issued between a `SubscribeRequest` and an `UnsubscribeRequest`.  
`X'06'` - `Ping`: A `Pong` MUST follow it.  
`X'07'` - `Pong`: A `Ping` MUST precede it.

### 2.2 Purpose

#### 2.2.1 Purpose of Request

The `Purpose` of `Request` is arbitrary.

#### 2.2.1 Purpose of SubscribeRequest, UnsubscribeRequest and Notification

The `Purpose` of `SubscribeRequest`, `UnsubscribeRequest` and `Notification` is arbitrary, but clients and servers MUST implement the same `Purpose` for all three.

#### 2.2.3 Purpose of Response

`X'00'` - `Success`  
`X'01'` - `BadRequest`: The request was malformed.  
`X'02'` - `VersionMismatch`  
`X'03'` - `UnsuccessfulRequest`: The server was not able to execute the `Request` properly.

`BadRequest` SHOULD be issued in case of client side errors, while `UnsuccessfulReqeust` SHOULD be issued in case of server side errors.  
`BadRequest` is issued for example, if the specified `ContentLength` does not match the actual length of the content, an arbitrary, user defined parameter does not match the expected format, or the `Purpose` of a `SubscribeRequest` is not recognized by the server.  
`UnsuccessfulRequest` is issued for example, if the server does not have the requested data available to `Response`.

#### 2.2.4 Purpose of Ping and Pong

The `Purpose` field of `Ping` MUST be `ping` and the `Purpose` field of `Pong` MUST be `pong`.

### 2.3 Content

`2147483647` is the maximum positive value for a 32-bit signed binary integer. `2147483647 - (1 + 1 + 1 + 4 + 255) = 2147483385` is the maximum number of bytes the `Content` field can hold. At deserialization, compliant implementations MUST validate the `ContentLength` field is within range. 

#### 2.3.1 Content as Error Details
If the `Response` is other than `Success`, the `Content` MAY hold the details of the error.  

* The server SHOULD use grammatically correct error messages.
* The server SHOULD write clear sentences and include ending punctuation.

## 3. Channel Isolation

The server and client MUST either communicate through a `RequestResponse` channel or a `SubscribeNotify` channel. A client and the server MAY have multiple channels open through different TCP connections. If these TCP connection were opened through Tor's SOCKS5 proxy with stream isolation, it can be used in a way, that the server does not learn the channels are originated from the same client.

The nature of the channel is defined by the first request of the client. If it is a `Request`, then the channel is a `RequestResponse` channel, if it is a `SubscribeRequest`, then the channel is a `SubscribeNotify` channel.

For a `Request` to a `SubscribeNotify` channel the server MUST respond with `BadRequest`, where the `Content` is: `Cannot send Request to a SubscribeNotify channel.`  
For a `SubscribeRequest` to a `RequestResponse` channel the server MUST respond with `BadRequest`, where the `Content` is: `Cannot send SubscribeRequest to a RequestResponse channel.`  

## 4. Keeping Channels Alive

Tor keeps its circuits alive, as long as they are used. Potentially forever. If a circuit fails, Tor will switch to a new circuit immediately. To make sure is a `SubscribeNotify` channel all notifications are delivered, the developer MAY consider implementing a redundant `SubscribeNotify` channel on an isolated stream.  

### 4.1 Ping Pong

| Version | MessageType | PurposeLength | Purpose | ContentLength | Content |
|---------|-------------|---------------|---------|---------------|---------|
| X'01'   | X'06'       | X'04'         | ping    | X'00000000'   |         |
| X'01'   | X'07'       | X'04'         | pong    | X'00000000'   |         |

In order to detect silent network failures, the client and the server SHOULD ping-pong periodically. For privacy against network observers, this ping-pong SHOULD happen randomly, by deafult every 1 to 10 minutes, but it SHOULD be adjustable based on the context of the specific application.

In a `RequestResponse` channel, the issuer of the ping is the client, while in a `SubscribeNotify` channel, the issuer of the ping is the server.

This ping-pong MAY be also used to quicktest network performance.

## 5. Closing the Channel

Closing the TCP connection both in `RequestResponse` and `SubscribeNotify` channels are a proper way to close the channel. In a `SubscribeNotify` channel, the client MAY issue the `UnsubscribeRequest`s, before closing the TCP connection.

## 6. Design Considerations

Tor sends data in chunks of 512 bytes, called cells, to make it harder for intermediaries to guess exactly how many bytes are being communicated at each step. A developer that intends to build an application on top of ToT MAY utilize this information to gain more efficient network usage by aiming the `ContentLength` to be minimum around `512 - (1 + 1 + 1 + 4) = 505` bytes minus the `Purpose` bytes.
