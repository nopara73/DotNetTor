# Tor over TCP (ToT)

## 1. Introduction

## 1.1 Purpose

ToT is a simple, application layer messaging protocol for this library, that makes TCP communication over Tor easier. ToT defines a request - response and subscribe - broadcast pattern.

## 1.2 Context

HTTP is the most commonly used application layer protcol. However HTTP fingerprinting makes it not ideal for privacy and the subscribe - broadcast pattern HTTP implementations are hacks.  
Tor is similar to a SOCKS5 proxy that is restricted to TCP. In order to exchange data through well known TCP connections, the connection must be estabilished through the Tor's SOCKS5 proxy first. If the connection is successful, TCP data exchange happens as usual. 

## 1.3 Requirements

### 1.3.1 UTF8

Use ToT uses with UTF8 byte encoding.
