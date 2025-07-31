# Database Value Searcher

A powerful, interactive command-line tool for searching and discovering data across SQL Server databases. Perfect for database administrators, developers, and data analysts who need to quickly locate specific values across multiple tables and columns without knowing the exact schema structure.

## 🌟 Features

### 🎯 **Smart Data Discovery**
- **Multi-Environment Support** - Seamlessly switch between Local, QA, and Production environments
- **Dynamic Database & Table Discovery** - Automatically lists available databases, tables, and views
- **Primary Key Integration** - Shows matching records with their primary key values for easy identification
- **Column-Level Search** - Searches across all string columns in selected tables/views

### 🔍 **Advanced Search Capabilities**
- **LIKE Pattern Matching** - Use SQL wildcards (`%`, `_`) for flexible searches
- **Regular Expression Support** - Full .NET regex support for complex pattern matching
- **Case-Sensitive Options** - Choose between case-sensitive and case-insensitive searches
- **Sample Data Preview** - See actual matching values with context

### 🎨 **Enhanced User Experience**
- **Color-Coded Output** - Easy-to-read results with syntax highlighting
- **Interactive Navigation** - Use `back`/`quit` commands to navigate between steps
- **Smart Error Recovery** - Graceful error handling without application crashes
- **Filtering & Search** - Filter large table lists by name

### 🔐 **Enterprise-Ready**
- **Production Safety** - Built-in confirmation prompts for production environments
- **Comprehensive Logging** - Detailed search timing and performance metrics
- **Connection Management** - Robust connection handling with automatic cleanup

## 🚀 Quick Start

### Prerequisites
- .NET 6.0 or later
- SQL Server access
- Visual Studio 2022 or VS Code (optional)

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/database-value-searcher.git
   cd database-value-searcher
   ```

2. **Install dependencies**
   ```bash
   dotnet restore
   ```

3. **Configure your environments**
   Edit `App.config` with your database connection strings:
   ```xml
   <connectionStrings>
     <add name="Local" 
          connectionString="Server=localhost;Integrated Security=true;TrustServerCertificate=true;" />
     <add name="QA" 
          connectionString="Server=qa-server;Database=QADatabase;User Id=qa_user;Password=qa_pass;" />
     <add name="Prod" 
          connectionString="Server=prod-server;Database=ProdDatabase;User Id=prod_user;Password=prod_pass;" />
   </connectionStrings>
   ```

4. **Build and run**
   ```bash
   dotnet build
   dotnet run
   ```

## 📖 Usage

### Interactive Mode (Recommended)
Simply run the application without arguments for a guided experience:

```bash
dotnet run
```

**Step-by-step flow:**
1. **Select Environment** → Local/QA/Production
2. **Choose Database** → From auto-discovered list
3. **Pick Table/View** → Browse or filter available objects
4. **Enter Search Pattern** → LIKE patterns or regex
5. **View Results** → Colored output with primary keys

### Command Line Mode
For automation and scripting:

```bash
# Basic LIKE search
dotnet run Local CustomerDB Users "%john%"

# Regular expression search
dotnet run QA OrdersDB Orders "\d{3}-\d{3}-\d{4}" --regex

# Phone number pattern search
dotnet run Prod ContactsDB Customers "^\d{10}$" --regex
```

## 🔍 Search Pattern Examples

### LIKE Patterns (SQL Wildcards)
| Pattern | Description | Example Matches |
|---------|-------------|-----------------|
| `john` | Exact match | john |
| `%john%` | Contains 'john' | johnson, mjohnson, john |
| `john%` | Starts with 'john' | johnson, johnny |
| `%john` | Ends with 'john' | mjohn, datajohn |
| `j_hn` | Single character wildcard | john, jahn |
| `%@%.com` | Email pattern | any email ending in .com |

### Regular Expressions
| Pattern | Description | Example Matches |
|---------|-------------|-----------------|
| `^john$` | Exact match | john |
| `.*john.*` | Contains 'john' | johnson, mjohnson |
| `john.*` | Starts with 'john' | johnson, johnny |
| `\d{3}-\d{3}-\d{4}` | Phone number | 123-456-7890 |
| `[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}` | Email address | user@domain.com |
| `^\d{5}(-\d{4})?$` | US ZIP code | 12345, 12345-6789 |

## 📊 Sample Output

```
✓ Column: CustomerName
  Type: varchar(100) NOT NULL  
  Matches: 15
  Search Time: 23 ms
  Sample Matches:
    • CustomerName: 'John Smith' | Keys: CustomerID=1001, CompanyID=500
    • CustomerName: 'Johnson Corp' | Keys: CustomerID=1234, CompanyID=501
    • CustomerName: 'Johnny Appleseed' | Keys: CustomerID=5678, CompanyID=502

MATCHING RECORDS SUMMARY:
----------------------------------------
  Record: CustomerID=1001, CompanyID=500
    Found in: CustomerName = 'John Smith'
  Record: CustomerID=1234, CompanyID=501
    Found in: CustomerName = 'Johnson Corp'
    
  Unique Records Found: 12
```

## ⚙️ Configuration

### App.config Structure
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <configSections>
    <section name="databaseEnvironments" type="System.Configuration.NameValueSectionHandler" />
  </configSections>
  
  <!-- Environment Display Names -->
  <databaseEnvironments>
    <add key="Local" value="Local Development Server" />
    <add key="QA" value="Quality Assurance Environment" />
    <add key="Prod" value="Production Environment" />
  </databaseEnvironments>
  
  <!-- Connection Strings -->
  <connectionStrings>
    <add name="Local" connectionString="Server=localhost;Integrated Security=true;TrustServerCertificate=true;" />
    <add name="QA" connectionString="Server=qa-server;User Id=qa_user;Password=qa_pass;" />
    <add name="Prod" connectionString="Server=prod-server;User Id=prod_user;Password=prod_pass;" />
  </connectionStrings>
  
  <!-- Application Settings -->
  <appSettings>
    <add key="DefaultMaxSamples" value="3" />
    <add key="RequireConfirmationForProd" value="true" />
    <add key="ShowTimingDetails" value="true" />
    <add key="MaxDisplayLength" value="50" />
  </appSettings>
</configuration>
```

### Customizable Settings
- **DefaultMaxSamples** - Number of sample records to display (default: 3)
- **RequireConfirmationForProd** - Require 'YES' confirmation for production (default: true)
- **ShowTimingDetails** - Display search timing information (default: true)
- **MaxDisplayLength** - Maximum length for displayed values (default: 50)

## 🛡️ Security Features

### Production Safety
- **Environment Indicators** - Clear [PRODUCTION] warnings
- **Confirmation Prompts** - Require explicit 'YES' confirmation
- **Read-Only Operations** - Only performs SELECT queries
- **Connection Security** - Supports encrypted connections

### Best Practices
- Use **read-only database accounts** for safety
- Configure **minimum required permissions**
- Enable **connection encryption** for production
- Store sensitive **connection strings securely**

## 🔧 Advanced Features

### Navigation Commands
- `back` - Return to previous step
- `quit` - Exit application  
- `f` - Filter tables/views by name

### Performance Optimization
- **Parallel column searching** for large tables
- **Efficient primary key retrieval**
- **Connection pooling** and cleanup
- **Memory-optimized regex processing**

### Error Handling
- **Graceful connection failures** with retry options
- **Invalid regex pattern validation**
- **Missing table/column detection**
- **Timeout handling** for large datasets

## 📋 Requirements

### System Requirements
- **.NET 8.0 or later**
- **Windows, macOS, or Linux**
- **Console/Terminal access**

### Database Requirements
- **SQL Server 2012 or later**
- **SQL Server Express, Standard, or Enterprise**
- **Azure SQL Database support**
- **Read access to target databases**

### NuGet Dependencies
```xml
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.1.0" />
<PackageReference Include="System.Configuration.ConfigurationManager" Version="7.0.0" />
```

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

### Development Setup
1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

### Coding Standards
- Follow **C# coding conventions**
- Add **XML documentation** for public methods
- Include **unit tests** for new features
- Maintain **backwards compatibility**

## 📝 License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.

## 🆘 Support

### Common Issues

**Connection Errors**
- Verify connection strings in App.config
- Check network connectivity to database server
- Ensure database user has appropriate permissions

**No Tables Found**
- Confirm you're connected to the correct database
- Verify the user has SELECT permissions on system tables
- Check if tables exist in 'dbo' schema (configurable)

**Performance Issues**
- Consider adding indexes to frequently searched columns
- Reduce sample size in configuration
- Use more specific search patterns to limit results

### Getting Help
- Check the [Issues](https://github.com/LarryCarter/DatabaseValueSearcher/issues) page
- Create a new issue with detailed error information
- Include your App.config (without sensitive connection details)

## 🎯 Use Cases

### Database Administration
- **Schema Discovery** - Map unknown database structures
- **Data Quality Analysis** - Find inconsistent or malformed data
- **Migration Planning** - Identify data patterns before migrations
- **Compliance Auditing** - Locate sensitive data across systems

### Development & Testing
- **Debugging** - Quickly locate test data records
- **Data Validation** - Verify expected values exist
- **Environment Synchronization** - Compare data across environments
- **Integration Testing** - Confirm data flows between systems

### Data Analysis
- **Pattern Discovery** - Find data patterns using regex
- **Data Profiling** - Understand data distribution and quality
- **Research & Investigation** - Locate specific records quickly
- **Business Intelligence** - Discover relationships in data

---

**If you have ever had to figure out where things in a database are without having any documentation, and need to find the data fast.**