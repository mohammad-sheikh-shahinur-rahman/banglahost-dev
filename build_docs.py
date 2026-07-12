import os
import json
import shutil
from datetime import datetime

DOCS_DIR = r"C:\xampp\htdocs\BanglaHost"
ASSETS_DIR = os.path.join(DOCS_DIR, "assets")

def ensure_dir(path):
    if not os.path.exists(path):
        os.makedirs(path)

ensure_dir(os.path.join(ASSETS_DIR, "css"))
ensure_dir(os.path.join(ASSETS_DIR, "js"))
ensure_dir(os.path.join(ASSETS_DIR, "images"))
ensure_dir(os.path.join(ASSETS_DIR, "icons"))
ensure_dir(os.path.join(ASSETS_DIR, "fonts"))
ensure_dir(os.path.join(DOCS_DIR, "screenshots"))

# 1. CSS
css_content = """
:root {
    --primary-color: #3B82F6; /* Modern vibrant blue */
    --primary-hover: #2563EB;
    --primary-light: rgba(59, 130, 246, 0.1);
    --bg-light: #F8FAFC; /* Sleek cool white */
    --bg-dark: #0F172A; /* Slate 900 for dark mode */
    --text-light: #1E293B; /* Slate 800 */
    --text-dark: #F1F5F9; /* Slate 100 */
    --sidebar-light: #FFFFFF;
    --sidebar-dark: #1E293B;
    --border-light: #E2E8F0;
    --border-dark: #334155;
    --code-bg-light: #F1F5F9;
    --code-bg-dark: #1E293B;
    --glass-bg-light: rgba(255, 255, 255, 0.85);
    --glass-bg-dark: rgba(15, 23, 42, 0.85);
}
body {
    font-family: 'Inter', -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
    transition: background-color 0.4s ease, color 0.4s ease;
    letter-spacing: -0.01em;
    line-height: 1.6;
}
body.light-mode { background-color: var(--bg-light); color: var(--text-light); }
body.dark-mode { background-color: var(--bg-dark); color: var(--text-dark); }

/* Glassmorphism Navbar */
.navbar {
    background-color: var(--glass-bg-dark) !important;
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
    border-bottom: 1px solid rgba(255, 255, 255, 0.1) !important;
    transition: background-color 0.4s ease, border-color 0.4s ease;
}
body.light-mode .navbar {
    background-color: var(--glass-bg-light) !important;
    border-bottom: 1px solid var(--border-light) !important;
}
body.light-mode .navbar-brand, body.light-mode .nav-link, body.light-mode .bi-list {
    color: var(--text-light) !important;
}
body.dark-mode .navbar-brand, body.dark-mode .nav-link, body.dark-mode .bi-list {
    color: #FFF !important;
}
body.light-mode #themeToggle { color: var(--text-light); }
body.dark-mode #themeToggle { color: #FFF; }

/* Sidebar */
.sidebar { transition: background-color 0.4s ease, border-right 0.4s ease; }
@media (min-width: 768px) {
    .sidebar {
        height: calc(100vh - 56px);
        position: sticky;
        top: 56px;
        overflow-y: auto;
        padding-top: 20px;
    }
    .sidebar::-webkit-scrollbar { width: 6px; }
    .sidebar::-webkit-scrollbar-thumb { background-color: var(--border-light); border-radius: 4px; }
    body.dark-mode .sidebar::-webkit-scrollbar-thumb { background-color: var(--border-dark); }
}
body.light-mode .sidebar {
    background-color: var(--sidebar-light);
    border-right: 1px solid var(--border-light);
    color: var(--text-light);
}
body.dark-mode .sidebar {
    background-color: var(--sidebar-dark);
    border-right: 1px solid var(--border-dark);
    color: var(--text-dark);
}

.sidebar .nav-link {
    color: inherit !important;
    padding: 10px 20px;
    border-radius: 8px;
    margin: 4px 12px;
    font-weight: 500;
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}
.sidebar .nav-link:hover {
    background-color: var(--primary-light);
    color: var(--primary-color) !important;
    transform: translateX(4px);
}
.sidebar .nav-link.active {
    background-color: var(--primary-color);
    color: #FFF !important;
    box-shadow: 0 4px 12px rgba(59, 130, 246, 0.3);
}

.sidebar-heading {
    font-size: 0.75rem;
    text-transform: uppercase;
    font-weight: 700;
    letter-spacing: 0.05em;
    padding: 15px 24px 5px;
    color: inherit;
    opacity: 0.5;
}

/* Content Area */
.content-area {
    padding: 40px 20px;
    max-width: 1000px;
    margin: 0 auto;
    animation: fadeIn 0.6s ease-out;
}
@keyframes fadeIn {
    from { opacity: 0; transform: translateY(10px); }
    to { opacity: 1; transform: translateY(0); }
}

.content-body {
    border-radius: 16px;
    box-shadow: 0 10px 40px rgba(0,0,0,0.03) !important;
    transition: background-color 0.4s ease, border-color 0.4s ease, box-shadow 0.4s ease;
}
body.dark-mode .content-body {
    background-color: var(--sidebar-dark) !important;
    border-color: var(--border-dark) !important;
    box-shadow: 0 10px 40px rgba(0,0,0,0.2) !important;
}

h1, h2, h3, h4 { font-weight: 700; margin-top: 2rem; margin-bottom: 1rem; color: inherit; }
h1 { font-size: 2.75rem; border-bottom: 2px solid var(--primary-light); padding-bottom: 12px; }
body.dark-mode h1 { border-bottom-color: var(--border-dark); }

/* Hero Section */
.hero {
    background: linear-gradient(135deg, #3B82F6 0%, #1D4ED8 100%);
    color: white;
    padding: 100px 20px;
    text-align: center;
    border-radius: 24px;
    margin-bottom: 50px;
    box-shadow: 0 20px 40px rgba(37, 99, 235, 0.2);
    position: relative;
    overflow: hidden;
}
.hero::after {
    content: '';
    position: absolute;
    top: 0; left: 0; right: 0; bottom: 0;
    background: radial-gradient(circle at top right, rgba(255,255,255,0.1) 0%, transparent 60%);
    pointer-events: none;
}

/* Cards Animation */
.card { 
    transition: all 0.4s cubic-bezier(0.4, 0, 0.2, 1);
    border-radius: 16px;
    border: 1px solid var(--border-light);
}
.card:hover {
    transform: translateY(-6px);
    box-shadow: 0 20px 40px rgba(0,0,0,0.08) !important;
    border-color: var(--primary-light);
}
body.dark-mode .card { 
    background-color: var(--sidebar-dark); 
    border-color: var(--border-dark); 
    color: var(--text-dark); 
}
body.dark-mode .card:hover {
    box-shadow: 0 20px 40px rgba(0,0,0,0.4) !important;
    border-color: rgba(59, 130, 246, 0.3);
}

/* Callouts */
.callout {
    padding: 20px 24px;
    margin: 28px 0;
    border-left: 6px solid;
    border-radius: 12px;
    font-size: 1.05rem;
}
body.light-mode .callout { background-color: #FFFFFF; box-shadow: 0 4px 20px rgba(0,0,0,0.04); }
body.dark-mode .callout { background-color: rgba(255,255,255,0.03); }
.callout-info { border-left-color: #3B82F6; }
.callout-warning { border-left-color: #F59E0B; }
.callout-danger { border-left-color: #EF4444; }
.callout-success { border-left-color: #10B981; }

/* Buttons */
.btn {
    border-radius: 10px;
    font-weight: 600;
    padding: 12px 28px;
    transition: all 0.3s ease;
}
.btn:hover { transform: translateY(-2px); box-shadow: 0 8px 20px rgba(0,0,0,0.1); }

/* Code Blocks */
pre { 
    border-radius: 12px; 
    padding: 20px; 
    position: relative; 
    font-family: 'Consolas', 'JetBrains Mono', 'Fira Code', monospace;
    font-size: 0.95rem;
}
body.light-mode pre { background-color: var(--code-bg-light); border: 1px solid var(--border-light); }
body.dark-mode pre { background-color: var(--code-bg-dark); border: 1px solid var(--border-dark); }
.copy-btn {
    position: absolute;
    top: 8px; right: 8px;
    background: var(--primary-light);
    color: var(--primary-color);
    border: none;
    padding: 6px 12px;
    border-radius: 6px;
    cursor: pointer;
    font-size: 0.8rem;
    font-weight: 600;
    transition: all 0.2s ease;
}
.copy-btn:hover { background: var(--primary-color); color: #FFF; }
"""
with open(os.path.join(ASSETS_DIR, "css", "style.css"), "w", encoding="utf-8") as f:
    f.write(css_content)

# 2. JS
js_content = """
document.addEventListener("DOMContentLoaded", () => {
    // Theme toggle
    const themeBtn = document.getElementById("themeToggle");
    const body = document.body;
    
    // Load preference
    const savedTheme = localStorage.getItem("theme") || "light-mode";
    body.className = savedTheme;
    updateThemeIcon(savedTheme);

    if (themeBtn) {
        themeBtn.addEventListener("click", () => {
            const isLight = body.classList.contains("light-mode");
            const newTheme = isLight ? "dark-mode" : "light-mode";
            body.className = newTheme;
            localStorage.setItem("theme", newTheme);
            updateThemeIcon(newTheme);
        });
    }

    function updateThemeIcon(theme) {
        if (!themeBtn) return;
        if (theme === "dark-mode") {
            themeBtn.innerHTML = '<i class="bi bi-sun-fill"></i>';
        } else {
            themeBtn.innerHTML = '<i class="bi bi-moon-fill"></i>';
        }
    }

    // Code copy buttons
    document.querySelectorAll("pre").forEach(pre => {
        const btn = document.createElement("button");
        btn.className = "copy-btn";
        btn.innerHTML = '<i class="bi bi-clipboard"></i> Copy';
        btn.onclick = () => {
            navigator.clipboard.writeText(pre.innerText.replace('Copy', '').trim());
            btn.innerHTML = '<i class="bi bi-check2"></i> Copied!';
            setTimeout(() => btn.innerHTML = '<i class="bi bi-clipboard"></i> Copy', 2000);
        };
        pre.appendChild(btn);
    });
    
    // Search
    const searchInput = document.getElementById("searchInput");
    const searchResults = document.getElementById("searchResults");
    
    if (searchInput) {
        fetch('/search.json')
            .then(res => res.json())
            .then(data => {
                searchInput.addEventListener('input', (e) => {
                    const q = e.target.value.toLowerCase();
                    if (!q) { searchResults.innerHTML = ''; return; }
                    
                    const matches = data.filter(item => 
                        item.title.toLowerCase().includes(q) || 
                        item.content.toLowerCase().includes(q)
                    ).slice(0, 5);
                    
                    if (matches.length > 0) {
                        searchResults.innerHTML = '<div class="list-group position-absolute w-100 shadow" style="z-index: 1000;">' + 
                            matches.map(m => `<a href="${m.url}" class="list-group-item list-group-item-action"><strong>${m.title}</strong></a>`).join('') +
                        '</div>';
                    } else {
                        searchResults.innerHTML = '<div class="list-group position-absolute w-100 shadow" style="z-index: 1000;"><div class="list-group-item">No results found</div></div>';
                    }
                });
                
                // Hide on click outside
                document.addEventListener('click', (e) => {
                    if (e.target !== searchInput) searchResults.innerHTML = '';
                });
            });
    }
});

// Service Worker Registration
if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('/service-worker.js');
    });
}
"""
with open(os.path.join(ASSETS_DIR, "js", "main.js"), "w", encoding="utf-8") as f:
    f.write(js_content)

# 3. PWA & SEO
manifest = {
  "name": "BanglaHost Documentation",
  "short_name": "BanglaHost Docs",
  "start_url": "/index.html",
  "display": "standalone",
  "background_color": "#ffffff",
  "theme_color": "#0078D4",
  "icons": []
}
with open(os.path.join(DOCS_DIR, "manifest.json"), "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2)

sw_content = """
const CACHE_NAME = 'banglahost-docs-v1';
const urlsToCache = [
  '/index.html',
  '/assets/css/style.css',
  '/assets/js/main.js'
];

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => cache.addAll(urlsToCache))
  );
});

self.addEventListener('fetch', event => {
  event.respondWith(
    caches.match(event.request)
      .then(response => response || fetch(event.request))
  );
});
"""
with open(os.path.join(DOCS_DIR, "service-worker.js"), "w", encoding="utf-8") as f:
    f.write(sw_content)
    
robots_content = "User-agent: *\nAllow: /\nSitemap: https://mohammad-sheikh-shahinur-rahman.github.io/banglahost-dev/sitemap.xml"
with open(os.path.join(DOCS_DIR, "robots.txt"), "w", encoding="utf-8") as f:
    f.write(robots_content)

# Define navigation structure and pages content
nav_groups = {
    "Getting Started": [
        {"title": "Home", "url": "index.html", "icon": "bi-house"},
        {"title": "Quick Start", "url": "quick-start.html", "icon": "bi-lightning"},
        {"title": "Installation", "url": "installation.html", "icon": "bi-download"},
        {"title": "Requirements", "url": "requirements.html", "icon": "bi-card-checklist"},
        {"title": "Configuration", "url": "configuration.html", "icon": "bi-gear"},
    ],
    "Core Features": [
        {"title": "Project Dashboard", "url": "project-dashboard.html", "icon": "bi-speedometer2"},
        {"title": "Environment Manager", "url": "environment-manager.html", "icon": "bi-sliders"},
        {"title": "Runtime Manager", "url": "runtime-manager.html", "icon": "bi-box"},
        {"title": "Performance Monitor", "url": "performance-monitor.html", "icon": "bi-graph-up"},
        {"title": "Terminal", "url": "terminal.html", "icon": "bi-terminal"},
        {"title": "Logs", "url": "logs.html", "icon": "bi-journal-text"},
    ],
    "Databases": [
        {"title": "Databases Overview", "url": "database.html", "icon": "bi-database"},
        {"title": "MySQL / MariaDB", "url": "mysql.html", "icon": "bi-database-fill"},
        {"title": "PostgreSQL", "url": "postgresql.html", "icon": "bi-database-fill"},
        {"title": "MongoDB", "url": "mongodb.html", "icon": "bi-database-fill"},
        {"title": "SQLite", "url": "sqlite.html", "icon": "bi-database-fill"},
        {"title": "Redis", "url": "redis.html", "icon": "bi-database-fill"},
    ],
    "Languages": [
        {"title": "PHP", "url": "php.html", "icon": "bi-code-slash"},
        {"title": "Python", "url": "python.html", "icon": "bi-filetype-py"},
        {"title": "Node.js", "url": "nodejs.html", "icon": "bi-filetype-js"},
        {"title": "Go", "url": "go.html", "icon": "bi-box-seam"},
        {"title": "Rust", "url": "rust.html", "icon": "bi-gear-wide-connected"},
    ],
    "Tools & Integrations": [
        {"title": "Git", "url": "git.html", "icon": "bi-git"},
        {"title": "Composer", "url": "composer.html", "icon": "bi-box"},
        {"title": "WP-CLI", "url": "wp-cli.html", "icon": "bi-wordpress"},
        {"title": "Docker", "url": "docker.html", "icon": "bi-box-seam"},
        {"title": "Mailpit", "url": "mailpit.html", "icon": "bi-envelope"},
        {"title": "AI Assistant (Ollama)", "url": "ai-assistant.html", "icon": "bi-robot"},
        {"title": "API Client", "url": "api-client.html", "icon": "bi-braces"},
        {"title": "Scheduler (Cron)", "url": "scheduler.html", "icon": "bi-clock"},
    ],
    "Networking": [
        {"title": "SSL Certificates", "url": "ssl.html", "icon": "bi-lock"},
        {"title": "Cloudflare Tunnel", "url": "cloudflare-tunnel.html", "icon": "bi-cloud"},
    ],
    "Site Management": [
        {"title": "Backup", "url": "backup.html", "icon": "bi-save"},
        {"title": "Restore", "url": "restore.html", "icon": "bi-arrow-counterclockwise"},
        {"title": "Clone Site", "url": "clone-site.html", "icon": "bi-files"},
    ],
    "Advanced": [
        {"title": "Security", "url": "security.html", "icon": "bi-shield-check"},
        {"title": "Architecture", "url": "architecture.html", "icon": "bi-diagram-3"},
        {"title": "Developer Guide", "url": "developer-guide.html", "icon": "bi-code"},
        {"title": "Troubleshooting", "url": "troubleshooting.html", "icon": "bi-wrench"},
        {"title": "FAQ", "url": "faq.html", "icon": "bi-question-circle"},
    ],
    "Project Info": [
        {"title": "Roadmap", "url": "roadmap.html", "icon": "bi-map"},
        {"title": "Contributing", "url": "contributing.html", "icon": "bi-people"},
        {"title": "About the Developer", "url": "developer.html", "icon": "bi-person-badge"},
        {"title": "Changelog", "url": "changelog.html", "icon": "bi-list-stars"},
        {"title": "Privacy Policy", "url": "privacy.html", "icon": "bi-shield-lock"},
        {"title": "License", "url": "license.html", "icon": "bi-file-earmark-text"},
    ]
}

pages_content = {
    "index.html": {
        "title": "BanglaHost Documentation",
        "content": '''
        <div class="hero">
            <h1 class="display-4 fw-bold">BanglaHost Documentation</h1>
            <p class="lead mb-4">The ultimate local web development environment for Windows. Built with .NET 8 and WinUI 3.</p>
            <div class="d-flex justify-content-center gap-3">
                <a href="https://apps.microsoft.com/store/detail/9MWFKR8D8318?cid=DevShareMCLPCS" class="btn btn-dark btn-lg">
                    <i class="bi bi-microsoft"></i> Get from Microsoft Store
                </a>
                <a href="https://mohammad-sheikh-shahinur-rahman.github.io/banglahost-dev/" class="btn btn-outline-light btn-lg">
                    <i class="bi bi-globe"></i> Visit Website
                </a>
                <a href="quick-start.html" class="btn btn-primary btn-lg shadow-sm">
                    <i class="bi bi-lightning-fill"></i> Quick Start
                </a>
            </div>
            <div class="mt-4">
                <span class="badge bg-secondary">Version 1.0.0</span>
                <span class="badge bg-primary">Platform: Windows</span>
            </div>
        </div>

        <div class="row g-4 mb-5">
            <div class="col-md-4">
                <div class="card h-100 shadow-sm border-0">
                    <div class="card-body text-center p-4">
                        <i class="bi bi-speedometer2 text-primary display-4 mb-3"></i>
                        <h3>Project Dashboard</h3>
                        <p class="text-muted">Manage all your local sites in one elegant WinUI 3 dashboard.</p>
                        <a href="project-dashboard.html" class="stretched-link"></a>
                    </div>
                </div>
            </div>
            <div class="col-md-4">
                <div class="card h-100 shadow-sm border-0">
                    <div class="card-body text-center p-4">
                        <i class="bi bi-box text-success display-4 mb-3"></i>
                        <h3>Runtime Manager</h3>
                        <p class="text-muted">Instantly install and switch between PHP, Python, Node, Go, and Rust.</p>
                        <a href="runtime-manager.html" class="stretched-link"></a>
                    </div>
                </div>
            </div>
            <div class="col-md-4">
                <div class="card h-100 shadow-sm border-0">
                    <div class="card-body text-center p-4">
                        <i class="bi bi-database text-info display-4 mb-3"></i>
                        <h3>Databases</h3>
                        <p class="text-muted">Built-in MySQL, PostgreSQL, MongoDB, SQLite, and Redis.</p>
                        <a href="database.html" class="stretched-link"></a>
                    </div>
                </div>
            </div>
        </div>
        
        <h2 class="mb-4">Features at a Glance</h2>
        <div class="row g-3">
            <div class="col-md-6">
                <ul class="list-group list-group-flush border rounded shadow-sm">
                    <li class="list-group-item"><i class="bi bi-check-circle-fill text-success me-2"></i> <strong>Multi-Language:</strong> PHP, Python, Node.js, Go, Rust</li>
                    <li class="list-group-item"><i class="bi bi-check-circle-fill text-success me-2"></i> <strong>Databases:</strong> MySQL, MariaDB, PostgreSQL, MongoDB, Redis, SQLite</li>
                    <li class="list-group-item"><i class="bi bi-check-circle-fill text-success me-2"></i> <strong>Web Servers:</strong> Nginx & Apache included out of the box</li>
                    <li class="list-group-item"><i class="bi bi-check-circle-fill text-success me-2"></i> <strong>Tools:</strong> Git, Composer, WP-CLI, Docker support</li>
                    <li class="list-group-item"><i class="bi bi-check-circle-fill text-success me-2"></i> <strong>Advanced:</strong> Mailpit, Cron Scheduler, API Client</li>
                </ul>
            </div>
            <div class="col-md-6">
                <ul class="list-group list-group-flush border rounded shadow-sm">
                    <li class="list-group-item"><i class="bi bi-check-circle-fill text-success me-2"></i> <strong>AI Assistant:</strong> Built-in Ollama integration for local AI</li>
                    <li class="list-group-item"><i class="bi bi-check-circle-fill text-success me-2"></i> <strong>Networking:</strong> Auto-SSL & Cloudflare Tunnels</li>
                    <li class="list-group-item"><i class="bi bi-check-circle-fill text-success me-2"></i> <strong>Management:</strong> Backup, Restore, and 1-Click Site Clone</li>
                    <li class="list-group-item"><i class="bi bi-check-circle-fill text-success me-2"></i> <strong>Monitoring:</strong> Real-time System Metrics & Performance Monitor</li>
                    <li class="list-group-item"><i class="bi bi-check-circle-fill text-success me-2"></i> <strong>Architecture:</strong> Modern .NET 8 + WinUI 3 with MVVM pattern</li>
                </ul>
            </div>
        </div>
        '''
    },
    "installation.html": {
        "title": "Installation Guide",
        "content": '''
        <h1>Installation Guide</h1>
        <p class="lead">Getting BanglaHost running on your Windows machine is incredibly simple.</p>
        
        <h2>Method 1: Microsoft Store (Recommended)</h2>
        <p>The easiest way to install and keep BanglaHost updated is via the Microsoft Store.</p>
        <ol>
            <li>Open the <strong>Microsoft Store</strong> app on Windows 10 or 11.</li>
            <li>Search for <strong>BanglaHost</strong> or click <a href="https://apps.microsoft.com/store/detail/9MWFKR8D8318?cid=DevShareMCLPCS">here</a>.</li>
            <li>Click <strong>Install</strong>.</li>
            <li>Launch the app from your Start Menu.</li>
        </ol>

        <h2>Method 2: Manual MSIX Installation</h2>
        <p>If you prefer not to use the Microsoft Store, you can download the <code>.msix</code> package directly.</p>
        <ol>
            <li>Download the latest release from the official website.</li>
            <li>Double-click the downloaded <code>.msix</code> file.</li>
            <li>Click <strong>Install</strong> in the Windows App Installer dialog.</li>
        </ol>

        <div class="callout callout-warning">
            <strong>Note:</strong> BanglaHost requires administrative privileges to modify your <code>hosts</code> file for local domains (e.g., <code>mysite.test</code>). You will be prompted by UAC when necessary.
        </div>
        '''
    },
    "quick-start.html": {
        "title": "Quick Start",
        "content": '''
        <h1>Quick Start</h1>
        <p class="lead">Create your first local development site in seconds.</p>
        
        <h2>1. Launch BanglaHost</h2>
        <p>Open the BanglaHost application from your Start Menu. On first launch, it will initialize the core services (Nginx, PHP, MySQL).</p>
        
        <h2>2. Create a Site</h2>
        <ol>
            <li>Navigate to the <strong>Sites</strong> tab in the sidebar.</li>
            <li>Click the <strong>Add Site</strong> button.</li>
            <li>Enter a site name (e.g., <code>myproject</code>). BanglaHost will automatically assign a domain like <code>myproject.test</code>.</li>
            <li>Select your preferred runtime (e.g., PHP, Node.js, Python).</li>
            <li>Click <strong>Create</strong>.</li>
        </ol>

        <h2>3. Access your Site</h2>
        <p>Once created, you can access your site at <code>http://myproject.test</code> or <code>https://myproject.test</code> in your browser.</p>

        <div class="callout callout-info">
            <strong>Tip:</strong> The document root for your new site is accessible via the "Open Folder" button in the site list dashboard.
        </div>
        '''
    },
    "requirements.html": {
        "title": "System Requirements",
        "content": '''
        <h1>System Requirements</h1>
        
        <h3>Minimum Requirements</h3>
        <table class="table table-striped table-bordered mt-3">
            <tbody>
                <tr><th scope="row">OS</th><td>Windows 10 (Version 1809 or higher) or Windows 11</td></tr>
                <tr><th scope="row">Architecture</th><td>x64 or ARM64</td></tr>
                <tr><th scope="row">RAM</th><td>4 GB (8 GB recommended for Databases & Docker)</td></tr>
                <tr><th scope="row">Disk Space</th><td>2 GB free space (SSD highly recommended)</td></tr>
                <tr><th scope="row">Framework</th><td>.NET 8 Desktop Runtime (Included in App Installer)</td></tr>
            </tbody>
        </table>
        '''
    },
    "architecture.html": {
        "title": "Architecture",
        "content": '''
        <h1>Architecture & Developer Guide</h1>
        <p>BanglaHost is a modern Windows desktop application built with <strong>.NET 8</strong> and <strong>WinUI 3 (Windows App SDK)</strong>.</p>
        
        <h2>Project Structure</h2>
        <pre><code class="language-plaintext">
src/
├── BanglaHost.App/       # WinUI 3 Frontend (Views, ViewModels, UI Services)
├── BanglaHost.Core/      # Core Business Logic (Engines, Downloader, DB Servers)
├── BanglaHost.Cli/       # Command Line Interface tools
└── BanglaHost.Elevate/   # UAC Elevation helper for modifying Hosts/Firewall
        </code></pre>

        <h2>Design Patterns</h2>
        <ul>
            <li><strong>MVVM (Model-View-ViewModel):</strong> Separates the UI (XAML) from the business logic. We use CommunityToolkit.Mvvm for ObservableObjects and RelayCommands.</li>
            <li><strong>Dependency Injection:</strong> Services like <code>SystemMetrics</code>, <code>SiteRepository</code>, and <code>Downloader</code> are injected via constructor.</li>
            <li><strong>Asynchronous Programming:</strong> Extensive use of <code>async/await</code> to keep the UI responsive while downloading runtimes or starting servers.</li>
        </ul>

        <h2>Runtime Detection & Management</h2>
        <p>The <code>BanglaHost.Core</code> project handles downloading and managing isolated environments for PHP, Node, Python, Go, and Rust. The <code>Downloader.cs</code> class fetches predefined binaries.</p>
        '''
    },
    "database.html": {
        "title": "Databases",
        "content": '''
        <h1>Databases Overview</h1>
        <p>BanglaHost provides built-in, isolated database servers that run seamlessly in the background without cluttering your system registry or services.</p>
        
        <div class="row mt-4">
            <div class="col-md-6 mb-3"><div class="card p-3 shadow-sm border-0"><a href="mysql.html" class="text-decoration-none h4 text-dark"><i class="bi bi-database-fill text-primary"></i> MySQL / MariaDB</a></div></div>
            <div class="col-md-6 mb-3"><div class="card p-3 shadow-sm border-0"><a href="postgresql.html" class="text-decoration-none h4 text-dark"><i class="bi bi-database-fill text-info"></i> PostgreSQL</a></div></div>
            <div class="col-md-6 mb-3"><div class="card p-3 shadow-sm border-0"><a href="mongodb.html" class="text-decoration-none h4 text-dark"><i class="bi bi-database-fill text-success"></i> MongoDB</a></div></div>
            <div class="col-md-6 mb-3"><div class="card p-3 shadow-sm border-0"><a href="sqlite.html" class="text-decoration-none h4 text-dark"><i class="bi bi-database-fill text-secondary"></i> SQLite</a></div></div>
            <div class="col-md-6 mb-3"><div class="card p-3 shadow-sm border-0"><a href="redis.html" class="text-decoration-none h4 text-dark"><i class="bi bi-database-fill text-danger"></i> Redis</a></div></div>
        </div>
        '''
    },
    "mysql.html": {
        "title": "MySQL & MariaDB",
        "content": '''
        <h1>MySQL & MariaDB</h1>
        <p>BanglaHost includes isolated instances of MySQL and MariaDB managed via <code>DbServer.cs</code>.</p>
        <h2>Default Credentials</h2>
        <ul>
            <li><strong>Host:</strong> 127.0.0.1</li>
            <li><strong>Port:</strong> 3306 (or configured in Settings)</li>
            <li><strong>User:</strong> root</li>
            <li><strong>Password:</strong> (empty by default)</li>
        </ul>
        '''
    },
    "postgresql.html": {
        "title": "PostgreSQL",
        "content": '''
        <h1>PostgreSQL</h1>
        <p>Managed via <code>PgServer.cs</code>. Fully isolated local PostgreSQL cluster.</p>
        <ul>
            <li><strong>Port:</strong> 5432</li>
            <li><strong>Default User:</strong> postgres</li>
        </ul>
        '''
    },
    "mongodb.html": {
        "title": "MongoDB",
        "content": '''
        <h1>MongoDB</h1>
        <p>NoSQL document database powered by <code>MongoServer.cs</code>.</p>
        <ul>
            <li><strong>Port:</strong> 27017</li>
        </ul>
        '''
    },
    "sqlite.html": {
        "title": "SQLite",
        "content": '''
        <h1>SQLite</h1>
        <p>BanglaHost perfectly supports SQLite databases out of the box. Since SQLite is file-based, no background service is required.</p>
        <p>PHP comes with the <code>pdo_sqlite</code> and <code>sqlite3</code> extensions pre-enabled.</p>
        '''
    },
    "redis.html": {
        "title": "Redis",
        "content": '''
        <h1>Redis Cache</h1>
        <p>In-memory data structure store, used as a database, cache, and message broker. Managed via <code>RedisManager.cs</code> and configured in the <code>RedisPage.xaml</code>.</p>
        <ul>
            <li><strong>Port:</strong> 6379</li>
        </ul>
        '''
    },
    "php.html": {
        "title": "PHP Environment",
        "content": '''
        <h1>PHP Environment</h1>
        <p>BanglaHost allows you to run multiple PHP versions simultaneously (via FastCGI / <code>PhpCgi.cs</code>).</p>
        <h2>Features</h2>
        <ul>
            <li>Switch PHP versions per site (e.g., Site A on PHP 7.4, Site B on PHP 8.3).</li>
            <li>Edit <code>php.ini</code> directly from the UI.</li>
            <li>Extensions like Xdebug, OPcache, cURL, OpenSSL enabled by default.</li>
        </ul>
        '''
    },
    "python.html": {
        "title": "Python",
        "content": '''
        <h1>Python</h1>
        <p>Run Python web applications (Flask, Django, FastAPI) natively on Windows via <code>PySite.cs</code>. BanglaHost manages virtual environments (venv) automatically.</p>
        '''
    },
    "nodejs.html": {
        "title": "Node.js",
        "content": '''
        <h1>Node.js</h1>
        <p>Deploy Node.js apps (Express, Next.js, Nuxt) easily using <code>NodeSite.cs</code>. Select Node versions dynamically.</p>
        '''
    },
    "go.html": {
        "title": "Go",
        "content": '''
        <h1>Go (Golang)</h1>
        <p>Compile and serve Go applications using <code>GoSite.cs</code>. Reverse proxy is automatically configured via Nginx.</p>
        '''
    },
    "rust.html": {
        "title": "Rust",
        "content": '''
        <h1>Rust</h1>
        <p>Run Rust web frameworks (Actix, Rocket) via <code>RustSite.cs</code> with automated port forwarding.</p>
        '''
    },
    "project-dashboard.html": {
        "title": "Project Dashboard",
        "content": '''
        <h1>Project Dashboard</h1>
        <p>The Dashboard (<code>DashboardPage.xaml</code>) is your command center.</p>
        <ul>
            <li>View all running sites.</li>
            <li>Access one-click buttons to open the site in browser, open the terminal, or open the folder.</li>
            <li>Monitor CPU and RAM usage via <code>SystemMetrics.cs</code>.</li>
        </ul>
        '''
    },
    "runtime-manager.html": {
        "title": "Runtime Manager",
        "content": '''
        <h1>Runtime Manager</h1>
        <p>The Runtime Manager (<code>RuntimesPage.xaml</code>) allows you to download and manage different versions of languages (PHP, Node, Python) without polluting your system PATH.</p>
        '''
    },
    "environment-manager.html": {
        "title": "Environment Manager",
        "content": '''
        <h1>Environment Manager</h1>
        <p>Manage global environment variables securely from the <code>EnvManagerPage.xaml</code>.</p>
        '''
    },
    "performance-monitor.html": {
        "title": "Performance Monitor",
        "content": '''
        <h1>Performance Monitor</h1>
        <p>Integrated directly into the Dashboard, powered by <code>SystemMetrics.cs</code>. Monitor real-time memory and CPU consumption of Nginx, Apache, Databases, and your application runtimes.</p>
        '''
    },
    "terminal.html": {
        "title": "Terminal",
        "content": '''
        <h1>Built-in Terminal</h1>
        <p>Use the <code>TerminalPage.xaml</code> to access PowerShell or CMD directly within BanglaHost. The terminal is automatically injected with the correct environment variables for the selected site.</p>
        '''
    },
    "logs.html": {
        "title": "Logs",
        "content": '''
        <h1>Logs Viewer</h1>
        <p>Access Nginx, Apache, PHP, and Database error/access logs in real-time from the <code>LogsPage.xaml</code>.</p>
        '''
    },
    "backup.html": {
        "title": "Site Backup",
        "content": '''
        <h1>Backup</h1>
        <p>Create zip archives of your entire site directory and database dump with a single click (implemented in <code>SiteListControl.xaml.cs</code>).</p>
        '''
    },
    "restore.html": {
        "title": "Site Restore",
        "content": '''
        <h1>Restore</h1>
        <p>Restore your site files and database from a previously generated BanglaHost backup archive.</p>
        '''
    },
    "clone-site.html": {
        "title": "Clone Site",
        "content": '''
        <h1>Clone Site</h1>
        <p>Duplicate an existing project to a new domain for safe staging and testing. The clone process copies files and duplicates the database automatically.</p>
        '''
    },
    "ssl.html": {
        "title": "SSL Certificates",
        "content": '''
        <h1>Auto SSL</h1>
        <p>BanglaHost automatically generates trusted self-signed certificates and configures Nginx (<code>NginxConfig.cs</code>) to serve your local sites over HTTPS without browser warnings.</p>
        '''
    },
    "cloudflare-tunnel.html": {
        "title": "Cloudflare Tunnel",
        "content": '''
        <h1>Cloudflare Tunnel</h1>
        <p>Share your local site with the world securely. Handled by <code>Tunnel.cs</code> and <code>TunnelService.cs</code>, you can expose your local <code>.test</code> domain to a public URL instantly via Cloudflare.</p>
        '''
    },
    "git.html": {
        "title": "Git Integration",
        "content": '''
        <h1>Git Integration</h1>
        <p>Manage your repository status, commit, and push directly from the <code>GitPage.xaml</code>.</p>
        '''
    },
    "docker.html": {
        "title": "Docker Integration",
        "content": '''
        <h1>Docker Support</h1>
        <p>Control Docker containers and view images from the <code>DockerPage.xaml</code>. BanglaHost interfaces with Docker Desktop on Windows.</p>
        '''
    },
    "composer.html": {
        "title": "Composer",
        "content": '''
        <h1>Composer (PHP)</h1>
        <p>The PHP package manager is pre-installed and available in the BanglaHost Terminal. Handled by <code>Downloader.cs</code>.</p>
        '''
    },
    "wp-cli.html": {
        "title": "WP-CLI",
        "content": '''
        <h1>WP-CLI</h1>
        <p>Manage your WordPress installations from the command line without configuring PHP paths manually.</p>
        '''
    },
    "mailpit.html": {
        "title": "Mailpit",
        "content": '''
        <h1>Local Email Testing</h1>
        <p>Catch outgoing emails from your web apps using Mailpit (<code>Mailpit.cs</code>). View sent emails in the web UI without actually sending them to real addresses.</p>
        '''
    },
    "ai-assistant.html": {
        "title": "AI Assistant (Ollama)",
        "content": '''
        <h1>AI Assistant (Ollama)</h1>
        <p>BanglaHost includes a built-in AI Assistant interface (<code>AiAssistantPage.xaml</code>) that connects to Ollama, allowing you to run local LLMs for code generation and assistance directly within the IDE.</p>
        '''
    },
    "api-client.html": {
        "title": "API Client",
        "content": '''
        <h1>API Client</h1>
        <p>Test REST APIs, GraphQL endpoints, and WebSockets directly from the <code>ApiClientPage.xaml</code>, similar to Postman.</p>
        '''
    },
    "scheduler.html": {
        "title": "Cron Scheduler",
        "content": '''
        <h1>Cron Scheduler</h1>
        <p>Run background tasks or scheduled scripts using <code>CronScheduler.cs</code> and <code>CronPage.xaml</code>. Perfect for WordPress cron or custom scheduled jobs.</p>
        '''
    },
    "security.html": {
        "title": "Security",
        "content": '''
        <h1>Security</h1>
        <p>BanglaHost uses a specialized elevation process (<code>BanglaHost.Elevate</code>) to modify the <code>hosts</code> file safely. Furthermore, it interacts with Windows Defender (<code>WindowsDefender.cs</code>) to add exclusions for project folders to improve I/O performance securely.</p>
        '''
    },
    "configuration.html": {
        "title": "Configuration",
        "content": '''
        <h1>Configuration</h1>
        <p>Global app settings can be adjusted in the <code>SettingsPage.xaml</code>. Configurations are saved as JSON files in the app data directory.</p>
        '''
    },
    "troubleshooting.html": {
        "title": "Troubleshooting",
        "content": '''
        <h1>Troubleshooting</h1>
        <ul>
            <li><strong>Port Conflicts:</strong> If Nginx fails to start, ensure Skype or IIS are not using port 80/443.</li>
            <li><strong>Hosts File Error:</strong> Ensure your Antivirus isn't blocking modifications to <code>C:\\Windows\\System32\\drivers\\etc\\hosts</code>.</li>
        </ul>
        '''
    },
    "faq.html": {
        "title": "FAQ",
        "content": '''
        <h1>Frequently Asked Questions</h1>
        <div class="accordion mt-4" id="faqAccordion">
            <div class="accordion-item">
                <h2 class="accordion-header"><button class="accordion-button" type="button" data-bs-toggle="collapse" data-bs-target="#collapseOne">Is BanglaHost free?</button></h2>
                <div id="collapseOne" class="accordion-collapse collapse show"><div class="accordion-body">Yes, the core application is free to use for local development.</div></div>
            </div>
            <div class="accordion-item">
                <h2 class="accordion-header"><button class="accordion-button collapsed" type="button" data-bs-toggle="collapse" data-bs-target="#collapseTwo">Can I use it for production?</button></h2>
                <div id="collapseTwo" class="accordion-collapse collapse"><div class="accordion-body">No, BanglaHost is designed strictly for local development environments on Windows.</div></div>
            </div>
        </div>
        '''
    },
    "roadmap.html": {
        "title": "Roadmap",
        "content": '''
        <h1>Roadmap</h1>
        <p>Upcoming features for BanglaHost include:</p>
        <ul>
            <li>macOS & Linux Support (via .NET MAUI)</li>
            <li>Advanced team sharing and sync</li>
            <li>Deeper Docker container orchestration</li>
        </ul>
        '''
    },
    "contributing.html": {
        "title": "Contributing",
        "content": '''
        <h1>Contributing</h1>
        <p>We welcome contributions! Please check the GitHub repository for open issues and submit Pull Requests. Ensure you follow our code style guidelines for C# / WinUI 3.</p>
        '''
    },
    "developer.html": {
        "title": "About the Developer",
        "content": '''
        <h1>About the Developer</h1>
        <div class="card mt-4 shadow-sm border-0">
            <div class="card-body p-5">
                <div class="row align-items-center">
                    <div class="col-md-4 col-lg-3 text-center mb-4 mb-md-0">
                        <img src="https://shahinurrahman.com/_next/image?url=https%3A%2F%2Fshahinurrahman.com%2Fshahinur.jpg&w=640&q=75" alt="Mohammad Sheikh Shahinur Rahman" class="img-fluid rounded-circle shadow-lg" style="width: 220px; height: 220px; object-fit: cover; border: 5px solid var(--primary-color);">
                    </div>
                    <div class="col-md-8 col-lg-9 text-center text-md-start">
                        <h2 class="fw-bold mb-1">Mohammad Sheikh Shahinur Rahman</h2>
                        <h5 class="text-primary mb-3">Creator & Lead Developer of BanglaHost</h5>
                        <p class="lead">
                            I am a passionate software architect, full-stack developer, and researcher dedicated to building high-performance, developer-friendly tools. My mission with BanglaHost is to revolutionize the local development experience on Windows by leveraging modern .NET 8 and WinUI 3 technologies.
                        </p>
                        <p>
                            Beyond software development, I am actively engaged in research and continuously exploring the intersection of modern system architecture and AI integrations to build smarter tools for the future.
                        </p>
                        <hr class="my-4">
                        <h5 class="mb-3">Connect with me:</h5>
                        <div class="d-flex flex-wrap gap-3">
                            <a href="https://shahinurrahman.com/" target="_blank" class="btn btn-outline-primary"><i class="bi bi-globe me-2"></i> Website</a>
                            <a href="https://www.linkedin.com/in/mohammad-sheikh-shahinur-rahman/" target="_blank" class="btn btn-outline-primary"><i class="bi bi-linkedin me-2"></i> LinkedIn</a>
                            <a href="https://www.researchgate.net/profile/Mohammad-Sheikh-Shahinur-Rahman" target="_blank" class="btn btn-outline-success"><i class="bi bi-journal-richtext me-2"></i> ResearchGate</a>
                        </div>
                        <div class="mt-4">
                            <strong><i class="bi bi-envelope-fill me-2 text-muted"></i> Emails:</strong>
                            <a href="mailto:info@shahinurrahman.com" class="text-decoration-none ms-2">info@shahinurrahman.com</a> | 
                            <a href="mailto:shahinalam3546@gmail.com" class="text-decoration-none">shahinalam3546@gmail.com</a>
                        </div>
                    </div>
                </div>
            </div>
        </div>
        '''
    },
    "changelog.html": {
        "title": "Changelog",
        "content": '''
        <h1>Changelog</h1>
        <p>Automatically synced from <code>CHANGELOG.md</code>.</p>
        <h3>Version 1.0.0</h3>
        <ul>
            <li>Initial release with PHP, Node, Python, Go, Rust support.</li>
            <li>Integrated MySQL, PostgreSQL, MongoDB, Redis, SQLite.</li>
            <li>WinUI 3 Dashboard implementation.</li>
        </ul>
        '''
    },
    "license.html": {
        "title": "License",
        "content": '''
        <h1>License</h1>
        <p>Please review the standard EULA and licensing terms provided in the application.</p>
        '''
    },
    "404.html": {
        "title": "Page Not Found",
        "content": '''
        <div class="text-center py-5">
            <h1 class="display-1 fw-bold text-primary">404</h1>
            <h2>Page Not Found</h2>
            <p class="lead">The documentation page you are looking for does not exist.</p>
            <a href="index.html" class="btn btn-primary mt-3">Return to Home</a>
        </div>
        '''
    }
}

# Template HTML
template = """<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{title} - BanglaHost Docs</title>
    <meta name="description" content="{title} for BanglaHost, the ultimate local web development environment.">
    <!-- SEO & PWA -->
    <link rel="manifest" href="/manifest.json">
    <meta name="theme-color" content="#3B82F6">
    <meta property="og:title" content="{title} - BanglaHost Docs">
    <meta property="og:type" content="website">
    <meta property="og:url" content="https://mohammad-sheikh-shahinur-rahman.github.io/banglahost-dev/">
    <meta property="og:image" content="https://mohammad-sheikh-shahinur-rahman.github.io/banglahost-dev/assets/images/og-image.png">
    <meta name="twitter:card" content="summary_large_image">
    <!-- Google Fonts -->
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
    <!-- Bootstrap CSS -->
    <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css" rel="stylesheet">
    <!-- Bootstrap Icons -->
    <link href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.1/font/bootstrap-icons.css" rel="stylesheet">
    <!-- Prism CSS (Syntax Highlighting) -->
    <link href="https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/themes/prism-tomorrow.min.css" rel="stylesheet">
    <!-- Custom CSS -->
    <link href="assets/css/style.css" rel="stylesheet">
</head>
<body class="light-mode">
    <!-- Header -->
    <nav class="navbar navbar-expand-lg fixed-top">
        <div class="container-fluid">
            <!-- Sidebar Toggler (Mobile Only) -->
            <button class="btn btn-link d-md-none border-0 me-2 text-decoration-none" type="button" data-bs-toggle="offcanvas" data-bs-target="#sidebarMenu" aria-controls="sidebarMenu">
                <i class="bi bi-list fs-3"></i>
            </button>
            
            <a class="navbar-brand fw-bold" href="index.html">
                <i class="bi bi-box-fill text-primary"></i> BanglaHost <span class="badge bg-primary rounded-pill ms-2 shadow-sm" style="font-size:0.7rem;">Docs</span>
            </a>
            
            <button class="navbar-toggler border-0" type="button" data-bs-toggle="collapse" data-bs-target="#navbarNav">
                <span class="navbar-toggler-icon"></span>
            </button>
            <div class="collapse navbar-collapse" id="navbarNav">
                <ul class="navbar-nav me-auto fw-medium">
                    <li class="nav-item"><a class="nav-link" href="https://apps.microsoft.com/store/detail/9MWFKR8D8318" target="_blank"><i class="bi bi-microsoft me-1"></i> Store</a></li>
                    <li class="nav-item"><a class="nav-link" href="https://mohammad-sheikh-shahinur-rahman.github.io/banglahost-dev/" target="_blank"><i class="bi bi-globe me-1"></i> Website</a></li>
                </ul>
                <div class="d-flex align-items-center position-relative me-3 mt-3 mt-lg-0">
                    <div class="input-group shadow-sm" style="border-radius: 8px; overflow: hidden;">
                        <span class="input-group-text bg-white border-0"><i class="bi bi-search text-muted"></i></span>
                        <input type="text" id="searchInput" class="form-control border-0 ps-0 shadow-none" placeholder="Search docs..." autocomplete="off">
                    </div>
                    <div id="searchResults" class="position-absolute w-100 top-100 mt-2"></div>
                </div>
                <button id="themeToggle" class="btn btn-link border-0 text-decoration-none mt-2 mt-lg-0 fs-5"><i class="bi bi-moon-fill"></i></button>
            </div>
        </div>
    </nav>

    <div class="container-fluid">
        <div class="row">
            <!-- Sidebar -->
            <nav class="col-md-3 col-lg-2 d-md-block sidebar offcanvas-md offcanvas-start" tabindex="-1" id="sidebarMenu" aria-labelledby="sidebarMenuLabel">
                <div class="offcanvas-header d-md-none border-bottom">
                    <h5 class="offcanvas-title" id="sidebarMenuLabel"><i class="bi bi-box-fill text-primary"></i> BanglaHost Docs</h5>
                    <button type="button" class="btn-close text-reset" data-bs-dismiss="offcanvas" data-bs-target="#sidebarMenu" aria-label="Close"></button>
                </div>
                <div class="offcanvas-body d-md-flex flex-column p-0 pt-lg-3 overflow-y-auto">
                    {sidebar}
                </div>
            </nav>

            <!-- Main Content -->
            <main class="col-md-9 ms-sm-auto col-lg-10 px-md-4 content-area pb-5">
                <nav aria-label="breadcrumb" class="mt-3">
                    <ol class="breadcrumb">
                        <li class="breadcrumb-item"><a href="index.html" class="text-decoration-none">Docs</a></li>
                        <li class="breadcrumb-item active" aria-current="page">{title}</li>
                    </ol>
                </nav>
                
                <div class="content-body shadow-sm bg-body rounded p-4 border" style="min-height: 70vh;">
                    {content}
                </div>
                
                <!-- Footer -->
                <footer class="mt-5 pt-4 border-top text-center text-muted">
                    <p>&copy; 2026 BanglaHost. Built for Windows. <a href="privacy.html" class="text-decoration-none text-muted ms-2">Privacy Policy</a></p>
                    <a href="#" class="text-decoration-none text-muted"><i class="bi bi-arrow-up-circle"></i> Back to Top</a>
                </footer>
            </main>
        </div>
    </div>

    <!-- Bootstrap Bundle JS -->
    <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/js/bootstrap.bundle.min.js"></script>
    <!-- Prism JS -->
    <script src="https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/prism.min.js"></script>
    <!-- Custom JS -->
    <script src="assets/js/main.js"></script>
</body>
</html>
"""

# Generate Sidebar HTML
sidebar_html = ""
for group_name, links in nav_groups.items():
    sidebar_html += f'<div class="sidebar-heading mt-3">{group_name}</div>\n<ul class="nav flex-column mb-2">\n'
    for link in links:
        sidebar_html += f'  <li class="nav-item"><a class="nav-link" href="{link["url"]}"><i class="bi {link["icon"]} me-2"></i>{link["title"]}</a></li>\n'
    sidebar_html += '</ul>\n'

# Generate Search JSON Data
search_data = []

# Write all pages
for filename, data in pages_content.items():
    html = template.replace("{title}", data["title"])
    html = html.replace("{sidebar}", sidebar_html)
    html = html.replace("{content}", data["content"])
    
    with open(os.path.join(DOCS_DIR, filename), "w", encoding="utf-8") as f:
        f.write(html)
        
    search_data.append({
        "title": data["title"],
        "url": filename,
        "content": data["content"].replace("\\n", " ").replace("'", "").replace('"', '')[:300]
    })

# Write search.json
with open(os.path.join(DOCS_DIR, "search.json"), "w", encoding="utf-8") as f:
    json.dump(search_data, f)

# Generate Sitemap
sitemap_urls = ""
for filename in pages_content.keys():
    sitemap_urls += f"  <url>\n    <loc>https://mohammad-sheikh-shahinur-rahman.github.io/banglahost-dev/{filename}</loc>\n  </url>\n"
sitemap_xml = f'<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n{sitemap_urls}</urlset>'
with open(os.path.join(DOCS_DIR, "sitemap.xml"), "w", encoding="utf-8") as f:
    f.write(sitemap_xml)

print("Documentation generated successfully!")
