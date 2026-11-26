Lavagon - bin folder
====================

Each tool lives in its own folder, one sub folder per version. Extract the
official Windows zip into a new sub folder and it shows up in Lavagon's Menu:

  bin\apache\httpd-2.4.xx-Win64-VS17\     https://www.apachelounge.com/download/
  bin\php\php-8.3.xx-Win32-vs16-x64\      https://windows.php.net/download/   (Thread Safe zip for Apache)
  bin\mysql\mysql-8.4.x-winx64\           https://dev.mysql.com/downloads/mysql/  (ZIP Archive)
  bin\mysql\mariadb-11.x-winx64\          https://mariadb.org/download/  (ZIP)
  bin\nginx\nginx-1.2x.x\                 https://nginx.org/en/download.html
  bin\redis\redis-x64-5.0.14.1\           https://github.com/tporadowski/redis/releases
  bin\mailpit\1.2x.x\                     https://github.com/axllent/mailpit/releases  (mailpit-windows-amd64.zip)
  bin\nodejs\node-v22\                    https://nodejs.org/en/download  (Windows zip)
  bin\python\python-3.13\                 https://www.python.org/downloads/windows/  (embeddable zip)
  bin\postgresql\postgresql-17\           https://www.enterprisedb.com/download-postgresql-binaries
  bin\mongodb\mongodb-8.0\                https://www.mongodb.com/try/download/community  (zip)
  bin\memcached\memcached-1.6.x\

Optional tools without versions:
  bin\composer\composer.phar              https://getcomposer.org/download/
  bin\heidisql\heidisql.exe               https://www.heidisql.com/download.php  (portable)
  bin\git\                                https://git-scm.com/download/win  (PortableGit)
  bin\cmder\Cmder.exe                     https://cmder.app/
  bin\notepad++\notepad++.exe             https://notepad-plus-plus.org/  (portable)

Lavagon never modifies anything inside bin\. All generated configuration lives
in etc\ and data in data\.
