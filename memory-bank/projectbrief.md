# Project Brief: Sekiban

## Overview
Sekiban is an Event Sourcing and CQRS (Command Query Responsibility Segregation) framework built with C# and .NET. It provides a declarative API for creating event sourcing applications with support for various event stores including Microsoft Azure Cosmos DB, Amazon DynamoDB, and PostgreSQL.

## Core Objectives
1. Simplify the implementation of Event Sourcing and CQRS patterns in .NET applications
2. Provide a flexible, type-safe API for defining domain models
3. Support multiple storage backends (Cosmos DB, DynamoDB, PostgreSQL)
4. Enable integration with Microsoft Orleans for distributed systems
5. Offer a clean, developer-friendly experience with minimal boilerplate

## Key Features
- Simple Commands and Events system
- Optimistic concurrency control with aggregate version checking
- Event versioning for forward compatibility
- Single and multi-aggregate projections
- Projection snapshots for performance optimization
- Support for large snapshots using cloud storage (Azure Blob Storage, Amazon S3)
- Built-in testing framework
- Multi-tenant support through partition keys
- Command and Query Web API generation with Swagger support

## Project Versions
The project has two main versions:
1. **Original Sekiban** - The initial implementation focusing on Azure Cosmos DB and AWS DynamoDB
2. **Sekiban.Pure** - A newer, more functional version with Microsoft Orleans integration

## Target Audience
- .NET developers implementing domain-driven design
- Teams building event-sourced systems
- Developers working with distributed systems
- Organizations using Azure, AWS, or PostgreSQL for data storage

## Project Status
The project is actively maintained by J-Tech Japan (株式会社ジェイテックジャパン), with ongoing development for new features and improvements. The newer Sekiban.Pure.Orleans version is currently under active development.

## License
Apache 2.0 open source license
