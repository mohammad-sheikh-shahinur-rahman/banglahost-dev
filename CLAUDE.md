You are a senior Windows desktop software architect, .NET security engineer,
DevOps engineer and QA engineer.

Audit this BanglaHost Local Web Server project comprehensively.

BanglaHost is a Windows local development environment similar to XAMPP/Laragon/WAMP.
It manages PHP, Nginx/Apache, MySQL/MariaDB, PostgreSQL, Redis, Node.js,
Composer, phpMyAdmin/Adminer, Mailpit, SSL, Cloudflare Tunnel and project
virtual hosts.

Audit objectives:

1. Find all potential bugs.
2. Find race conditions and async/await problems.
3. Find UI thread blocking operations.
4. Find NullReferenceException risks.
5. Find unhandled exceptions.
6. Find memory leaks.
7. Find process/resource leaks.
8. Find orphaned PHP/Nginx/MySQL/Node processes.
9. Find incorrect process lifecycle management.
10. Audit file and directory permissions.
11. Audit command execution and ProcessStartInfo usage.
12. Detect command injection risks.
13. Detect path traversal risks.
14. Detect unsafe shell/PowerShell execution.
15. Audit localhost/network exposure.
16. Audit firewall and port handling.
17. Audit SSL/certificate handling.
18. Audit configuration file generation.
19. Audit virtual host generation.
20. Audit concurrent project operations.
21. Audit service start/stop/restart logic.
22. Audit cancellation token usage.
23. Audit timeout handling.
24. Audit logging.
25. Audit crash recovery.
26. Audit startup/shutdown lifecycle.
27. Audit installer/MSIX behavior.
28. Audit update mechanism.
29. Audit dependency vulnerabilities.
30. Audit performance.
31. Audit UI/UX reliability.
32. Audit accessibility.
33. Audit localization.
34. Audit backup/recovery.
35. Audit database lifecycle.
36. Audit Cloudflare Tunnel integration.
37. Audit Mailpit integration.
38. Audit PHP version switching.
39. Audit Nginx/Apache switching.
40. Audit project creation/deletion.

For every issue provide:

- Severity: Critical / High / Medium / Low
- File
- Class/Method
- Exact problematic code
- Why it is a problem
- Reproduction scenario
- Recommended fix
- Suggested corrected code
- Regression test

Do NOT invent issues.
If something cannot be verified from the source code, mark it as:
"Requires runtime verification".

Finally produce:

A. Critical issues
B. Security issues
C. Crash/hang issues
D. Performance issues
E. Architecture issues
F. UX issues
G. Technical debt
H. Recommended fixes
I. Test plan
J. Release readiness score /100