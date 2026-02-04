# Security Policy

## Supported Versions

We release patches for security vulnerabilities for the following versions:

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |

## Reporting a Vulnerability

If you discover a security vulnerability in the BizTalk Migration Starter toolkit, please report it responsibly:

### ?? Private Disclosure

**DO NOT** open a public GitHub issue for security vulnerabilities.

Instead, please email security reports to:
- **Email**: Create a private security advisory via GitHub's Security tab

Or use GitHub's private vulnerability reporting:
1. Navigate to the [Security tab](https://github.com/haroldcampos/BizTalkMigrationStarter/security)
2. Click "Report a vulnerability"
3. Provide detailed information about the issue

### ?? What to Include

When reporting a vulnerability, please include:
- Description of the vulnerability
- Steps to reproduce the issue
- Potential impact
- Suggested fix (if available)
- Your contact information

### ?? Response Timeline

- **Initial Response**: Within 48 hours of report submission
- **Status Update**: Within 7 days with assessment and timeline
- **Fix Timeline**: Critical issues within 30 days, others within 90 days

### ?? Security Best Practices for Users

When using the BizTalk Migration Starter toolkit:

#### 1. **Protect BizTalk Binding Files**

**?? NEVER commit BizTalk binding files with real credentials to version control**

```xml
<!-- ? BAD: Real credentials in binding file -->
<UserName>sa</UserName>
<Password>MyRealPassword123!</Password>
<ConnectionString>Server=prod-sql;Database=BizTalk;User Id=admin;Password=RealPass;</ConnectionString>
```

```xml
<!-- ? GOOD: Use placeholders -->
<UserName>__USERNAME_PLACEHOLDER__</UserName>
<Password>__PASSWORD_PLACEHOLDER__</Password>
<ConnectionString>__CONNECTION_STRING_PLACEHOLDER__</ConnectionString>
```

#### 2. **Use Secure Secret Management**

Store actual credentials using:
- **Azure Key Vault** (recommended for cloud deployments)
- **Environment variables** (for local development)
- **Secure configuration management systems** (HashiCorp Vault, AWS Secrets Manager)

#### 3. **Sanitize Generated Workflows**

Before committing generated Logic Apps workflows:

```bash
# Review generated workflow.json for embedded secrets
# Search for patterns like:
grep -i "password\|secret\|connectionstring" workflow.json
```

#### 4. **Protect Local Binding Files**

Add to your project's `.gitignore`:

```gitignore
# Local BizTalk binding files with real credentials
**/bindings.local.xml
**/bindings.prod.xml
**/credentials.xml
**/*.private.xml
```

#### 5. **Validate Generated Output**

Always review generated Logic Apps workflows before deployment:
- Check for hardcoded credentials
- Verify connection strings are parameterized
- Ensure sensitive data uses secure parameters

#### 6. **Use Latest Version**

Always use the latest version of the toolkit to benefit from security patches:

```bash
# Check for updates
git pull origin main
```

## ?? Security Features

This toolkit implements the following security measures:

### Code Security
- ? No hardcoded secrets or credentials
- ? No unsafe code blocks
- ? No dynamic code execution (eval, reflection abuse)
- ? All XML parsing uses safe, built-in .NET libraries
- ? No SQL injection vectors (no database operations)

### Dependency Security
- ? Uses well-maintained, trusted NuGet packages
- ? Minimal external dependencies
- ? Regular dependency updates via Dependabot

### Data Protection
- ? Credentials abstracted as properties (never hardcoded)
- ? All sensitive data passed via parameters
- ? No logging of sensitive information

## ??? Known Security Considerations

### 1. **BizTalk Binding Files**
- Binding XML files may contain sensitive information (usernames, passwords, connection strings)
- **Your responsibility**: Sanitize binding files before using this tool
- **Recommendation**: Use placeholder values in binding files committed to repos

### 2. **Generated Workflow Files**
- Generated `workflow.json` files may contain connection information
- **Your responsibility**: Review generated files before committing to version control
- **Recommendation**: Use Logic Apps parameters for all connection strings

### 3. **Local Development**
- Test data (sample ODX, BTM, BTP files) should not contain production credentials
- **Your responsibility**: Use sanitized test data
- **Recommendation**: Create dedicated test fixtures with dummy credentials

## ?? Additional Resources

- [OWASP Secure Coding Practices](https://owasp.org/www-project-secure-coding-practices-quick-reference-guide/)
- [Azure Key Vault Documentation](https://learn.microsoft.com/azure/key-vault/)
- [GitHub Security Best Practices](https://docs.github.com/en/code-security)
- [BizTalk Security Best Practices](https://learn.microsoft.com/biztalk/core/security-recommendations)

## ?? Contact

For security-related questions or concerns:
- **GitHub Security Advisory**: Use the Security tab in this repository
- **General Questions**: Open a discussion in GitHub Discussions

---

**Last Updated**: January 2025  
**Version**: 1.0.0
