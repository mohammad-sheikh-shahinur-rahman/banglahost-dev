using System;
using System.IO;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public static class GithubActionsService
{
    private const string LaravelWorkflow = @"name: Laravel CI
on:
  push:
    branches: [ ""main"", ""master"" ]
  pull_request:
    branches: [ ""main"", ""master"" ]
jobs:
  laravel-tests:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Setup PHP
      uses: shivammathur/setup-php@v2
      with:
        php-version: '8.2'
    - name: Copy .env
      run: php -r ""file_exists('.env') || copy('.env.example', '.env');""
    - name: Install Dependencies
      run: composer install -q --no-ansi --no-interaction --no-scripts --no-progress --prefer-dist
    - name: Generate key
      run: php artisan key:generate
    - name: Directory Permissions
      run: chmod -R 777 storage bootstrap/cache
    - name: Execute tests
      run: vendor/bin/phpunit
";

    private const string NodejsWorkflow = @"name: Node.js CI
on:
  push:
    branches: [ ""main"", ""master"" ]
  pull_request:
    branches: [ ""main"", ""master"" ]
jobs:
  build:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        node-version: [16.x, 18.x, 20.x]
    steps:
    - uses: actions/checkout@v3
    - name: Use Node.js ${{ matrix.node-version }}
      uses: actions/setup-node@v3
      with:
        node-version: ${{ matrix.node-version }}
        cache: 'npm'
    - run: npm ci
    - run: npm run build --if-present
    - run: npm test
";

    private const string PhpWorkflow = @"name: PHP CI
on:
  push:
    branches: [ ""main"", ""master"" ]
  pull_request:
    branches: [ ""main"", ""master"" ]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Setup PHP
      uses: shivammathur/setup-php@v2
      with:
        php-version: '8.2'
    - name: Validate composer.json and composer.lock
      run: composer validate --strict
    - name: Cache Composer packages
      id: composer-cache
      uses: actions/cache@v3
      with:
        path: vendor
        key: ${{ runner.os }}-php-${{ hashFiles('**/composer.lock') }}
        restore-keys: |
          ${{ runner.os }}-php-
    - name: Install dependencies
      run: composer install --prefer-dist --no-progress
";

    private const string ReactWorkflow = @"name: React CI
on:
  push:
    branches: [ ""main"", ""master"" ]
  pull_request:
    branches: [ ""main"", ""master"" ]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Use Node.js
      uses: actions/setup-node@v3
      with:
        node-version: 18.x
    - run: npm ci
    - run: npm run build
    - run: npm test --if-present
";

    private const string VueWorkflow = @"name: Vue CI
on:
  push:
    branches: [ ""main"", ""master"" ]
  pull_request:
    branches: [ ""main"", ""master"" ]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Use Node.js
      uses: actions/setup-node@v3
      with:
        node-version: 18.x
    - run: npm ci
    - run: npm run build
    - run: npm test --if-present
";

    private const string WordPressWorkflow = @"name: WordPress CI
on:
  push:
    branches: [ ""main"", ""master"" ]
  pull_request:
    branches: [ ""main"", ""master"" ]
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Setup PHP
      uses: shivammathur/setup-php@v2
      with:
        php-version: '8.2'
    - name: Run syntax check
      run: find . -name '*.php' -exec php -l {} \;
";

    private const string PythonWorkflow = @"name: Python CI
on:
  push:
    branches: [ ""main"", ""master"" ]
  pull_request:
    branches: [ ""main"", ""master"" ]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Set up Python
      uses: actions/setup-python@v4
      with:
        python-version: '3.10'
    - name: Install dependencies
      run: |
        python -m pip install --upgrade pip
        if [ -f requirements.txt ]; then pip install -r requirements.txt; fi
";

    private const string HtmlWorkflow = @"name: Static HTML CI
on:
  push:
    branches: [ ""main"", ""master"" ]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Check HTML
      run: echo 'HTML looks good!'
";

    public static async Task<bool> ScaffoldWorkflowAsync(string projectPath, string type, Action<string> log)
    {
        var workflowsDir = Path.Combine(projectPath, ".github", "workflows");
        try
        {
            if (!Directory.Exists(workflowsDir))
            {
                Directory.CreateDirectory(workflowsDir);
                log("Created .github/workflows directory.");
            }

            var filePath = Path.Combine(workflowsDir, $"{type.ToLower()}-ci.yml");
            if (File.Exists(filePath))
            {
                log($"Workflow {filePath} already exists. Skipping.");
                return false;
            }

            string content = type.ToLower() switch
            {
                "laravel" => LaravelWorkflow,
                "node" => NodejsWorkflow,
                "php" => PhpWorkflow,
                "react" => ReactWorkflow,
                "vue" => VueWorkflow,
                "wordpress" => WordPressWorkflow,
                "python" => PythonWorkflow,
                "html" => HtmlWorkflow,
                _ => throw new ArgumentException("Unknown workflow type")
            };

            await File.WriteAllTextAsync(filePath, content);
            log($"Successfully generated {type} CI workflow at {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            log($"Error scaffolding workflow: {ex.Message}");
            return false;
        }
    }
}
