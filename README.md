# Web_download
### web服务，用于通过选择浏览器上的浏览器列表里的文件，下载该文件
### 在/Propertis/launchSettings.json设置服务端口及域名
### 支持单文件下载、选中多文件经压缩后下载
### 服务端/wwwroot/Uploads目录下为文件下载目录，需下载的文件放在该目录，局域网内被下载的文件需断点续传时，直接在浏览器输入https://IP+端口/Uploads/要下载的文件名.后缀名
### 支持https,断点续传，需手动生成证书（例如使用openssl生成）
#### 生成证书。先生成如smydownload.key、smydownload.crt（先生成配置文件.cnf）,再生成代码引用的smydownload.pfx文件（过程需两次输入密码，放在根目录）
#### openssl req -new -x509 -days 365 -config smydownload.cnf -keyout smydownload.key -out smydownload.crt
#### openssl pkcs12 -export -in smydownload.crt -inkey smydownload.key -out smydownload.pfx -name "websiteandIp"
#### 配置文件如smydownload.cnf,（关键：定义 subjectAltName 包含 IP）
##### [req_distinguished_name]
##### C = CN
##### ST = Beijing
##### L = Beijing
##### O = SmyDownload Dev 
##### OU = IT 
##### CN = www.smydownload.com
 
##### [v3_req]
##### keyUsage = critical, digitalSignature, keyEncipherment
##### extendedKeyUsage = serverAuth
##### subjectAltName = @server_names
 
##### [server_names]
##### DNS.1 = www.smydownload.com
##### DNS.2 = smydownload.com 
##### DNS.3 = localhost
##### IP.1 = 127.0.0.1
##### IP.1 = 192.168.3.2
