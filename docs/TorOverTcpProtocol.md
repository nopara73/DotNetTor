# Tor over TCP (ToT)

## 1. Introduction

## 1.1 Purpose

ToT is a simple, application layer messaging protocol for this library, that makes TCP communication over Tor easier. ToT defines a request - response and subscribe - broadcast pattern.

## 1.2 Context

HTTP is the most commonly used application layer protcol. However HTTP fingerprinting makes it not ideal for privacy and the subscribe - broadcast pattern HTTP implementations are hacks.  
Tor is similar to a SOCKS5 proxy that is restricted to TCP. In order to exchange data through well known TCP connections, the connection must be estabilished through the Tor's SOCKS5 proxy first. If the connection is successful, TCP data exchange happens as usual. 

## 1.3 Notation

### 1.3.1 Keywords

The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY", and "OPTIONAL" in this document are to be interpreted as described in RFC2119.

### 1.3.2 Packet-format Diagrams

Unless otherwise noted, the decimal numbers appearing in packet-format diagrams represent the length of the corresponding field, in octets. Where a given octet must take on a specific value, the syntax X'hh' is used to denote the value of the single octet in that field.

## 1.4 Requirements

### 1.4.1 UTF8

ToT uses UTF8 byte encoding.

## 2. Message Format

| Version | MessageType | PurposeLength | Purpose | ContentLength | Content      |
|---------|-------------|---------------|---------|---------------|--------------|
| X'01'   | 1           | 1             | 0-255   | 4             | 0-4294967295 |

### 2.1 MessageType

X'01' - Request: A Reply MUST follow it.  
X'02' - Reply: A Request MUST precede it.  
X'03' - SubscribeRequest: A Reply MUST follow it.

### 2.2 Purpose

The Purpose of Request and Subscribe is arbitrary.

### 2.2.1 Purpose of Reply

X'00' - Success
X'01' - BadRequest: The request was malformed.  
X'02' - VersionMismatch  
X'03' - UnsuccessfulRequest: The server was not able to create a proper Reply to the Request.

If the reply is other than Success, the Content may holds the details of the error.

BadRequest SHOULD be issued in case of client side errors, while UnsuccessfulReqeust SHOULD be issued in case of server side errors.  
BadRequest is issued for example, if the specified ContentLength does not match the actual length of the content, an arbitrary, user defined parameter doesn't match the expected format, or the Purpose of a SubscribeRequest is not recognized by the server.  
UnsuccessfulRequest is issued for example, if the server doesn't have the requested data available to reply.
