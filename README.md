# Game Trackr Backend 🎮⚙️

### **Final Degree Project | Computer Engineering**

This repository contains the backend infrastructure for **Game Trackr**, a video game management and discovery platform. Developed as my **Final Degree Project**, this backend focuses on building a scalable, containerized architecture that integrates relational data, semantic search, and local AI processing.

---

## 🏗️ Technical Architecture

The system is designed as a multi-service ecosystem orchestrated via **Docker Compose**, ensuring high availability and parity between environments.

* **Core API**: Built with **ASP.NET Core (.NET 8)**, utilizing a multi-stage Docker build for optimized runtime performance.
* **Vector Search**: Integration with **Weaviate** to enable semantic search capabilities.
* **Local AI Integration**: Orchestrates **Ollama** for local Large Language Model (LLM) processing.
* **Reverse Proxy**: **Nginx** configuration for secure traffic routing and certificate management.
* **Relational Database**: **PostgreSQL** handles structured data with dedicated persistence volumes.
* **Automated SSL**: Integrated **Certbot** for Let's Encrypt certificate issuance and renewal.

## 🛠️ Tech Stack

| Component | Technology |
| :--- | :--- |
| **Runtime** | .NET 8 (ASP.NET Core) |
| **Relational DB** | PostgreSQL |
| **Vector DB** | Weaviate |
| **AI Runtime** | Ollama |
| **Web Server** | Nginx |
| **DevOps** | Docker & Docker Compose |

## 🐳 Infrastructure & DevOps

### **Optimized Containerization**
The project implements a **multi-stage Dockerfile** to separate the build environment from the production runtime. 
* **Build Stage**: Uses `dotnet/sdk` to restore dependencies and publish the application.
* **Runtime Stage**: Uses `dotnet/aspnet` for a lightweight, secure production image.
* **Volume Mapping**: Ensures persistence for database logs, AI data, and SSL certificates.

### **System Hygiene**
* **Exclusion Rules**: Strict `.dockerignore` policies prevent local `bin/`, `obj/`, and IDE settings from bloating the image.
* **Git Security**: Comprehensive `.gitignore` protects sensitive `.env` files and local developer configurations.

## 🚀 Getting Started

1. **Environment**: Ensure Docker and Docker Compose are installed.
2. **Launch**:
   ```bash
   docker-compose up -d
