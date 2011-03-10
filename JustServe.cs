using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace just_serve
{
    public class JustServe
    {
        public const string SERVERNAME = "justserve 1.0";

        Dictionary<string, string> MimeTypes = new Dictionary<string, string>() 
        { 
            { "atom", "application/atom+xml" },
            { "json", "application/json" },
            { "js", "application/javascript" },
            { "xhtml", "application/xhtml+xml" },
            { "gif", "image/gif" },
            { "jpeg", "image/jpeg" },
            { "jpg", "image/jpeg" },
            { "png", "image/png" },
            { "svg", "image/svg+xml" },
            { "tiff", "image/tiff" },
            { "css", "text/css" },
            { "htm", "text/html; charset=utf-8" },
            { "html", "text/html; charset=utf-8" },
            { "xml", "text/xml" },
        };

        public delegate void Callback(Request request, Response response);

        #region Private variables
        private List<Responder> responders = new List<Responder>();
        private TcpListener listener;
        private Callback NotFoundCallBack;
        #endregion

        public string LocalPath { get; set; }

        public JustServe(string LocalPath) 
        {
            this.LocalPath = LocalPath.TrimEnd('\\', '/');
            NotFoundCallBack = (req, resp) =>
                {
                    resp.End("404 Not Found");
                };
        }

        public void Get(string expr, Callback callback)
        {
            responders.Add(new Responder(Verb.GET, expr, callback));
        }

        public void Post(string expr, Callback callback)
        {
            responders.Add(new Responder(Verb.POST, expr, callback));
        }

        public void Put(string expr, Callback callback)
        {
            responders.Add(new Responder(Verb.PUT, expr, callback));
        }

        public void Delete(string expr, Callback callback)
        {
            responders.Add(new Responder(Verb.DELETE, expr, callback));
        }

        public void NotFound(Callback Callback)
        {
            NotFoundCallBack = Callback;
        }

        public void Listen(string Address, int Port)
        {
            if (Address.ToLower() == "localhost") { Address = "127.0.0.1"; }
            listener = new TcpListener(IPAddress.Parse(Address), Port);
            listener.Start();

            new Thread(() =>
                {
                    while (true)
                    {
                        using (Socket socket = listener.AcceptSocket())
                        {
                            if (socket.Connected)
                            {
                                byte[] bytes = new byte[1024];
                                int received = socket.Receive(bytes, bytes.Length, 0);
                                string buffer = Encoding.UTF8.GetString(bytes);

                                string[] sections = buffer.Split(System.Environment.NewLine.ToCharArray());

                                Request req = new Request();
                                for (int i = 0; i < sections.Length; i++)
                                {
                                    string section = sections[i].Trim();
                                    Match matchKey = Regex.Match(section, "^(.+?)(?=([ :]))");
                                    if (matchKey.Success)
                                    {
                                        string key = matchKey.Value;
                                        string value = section.Replace(key, "").TrimStart(' ', ':');
                                        SetRequestHeader(key, value, ref req);
                                    }
                                }

                                // Find the callback.
                                Responder responder = responders.FirstOrDefault(r => r.Verb == req.Verb && Regex.IsMatch(req.Path, r.Expression));

                                if (!responder.IsEmpty())
                                    responder.Callback(req, new Response(socket, 200));
                                else if (FileExists(req.Path))
                                    ServeFile(req, new Response(socket, 200));
                                else
                                    NotFoundCallBack(req, new Response(socket, 404));
                            }
                        }
                    }
                }).Start();
        }

        public void ServeFile(string RelativePath, Request request, Response response)
        {
            string FullPath = LocalPath + RelativePath.Replace('/', '\\');
            FileStream fs = new FileStream(FullPath, FileMode.Open, FileAccess.Read);
            BinaryReader br = new BinaryReader(fs);
            long numBytes = new FileInfo(FullPath).Length;
            byte[] buffer = br.ReadBytes((int)numBytes);
            string ext = FullPath.Substring(FullPath.LastIndexOf('.') + 1);

            response.ContentType = MimeTypes.FirstOrDefault(a => a.Key == ext).Value;
            response.End(buffer);
        }

        private void ServeFile(Request request, Response response)
        {
            ServeFile(request.Path, request, response);
        }

        private bool FileExists(string Path)
        {
            Path = LocalPath + Path.Replace('/', '\\');
            return File.Exists(Path);
        }

        private void SetRequestHeader(string key, string value, ref Request req)
        {
            switch(key.ToLower())
            {
                case "get":
                    req.Verb = Verb.GET;
                    goto Path;
                case "post":
                    req.Verb = Verb.POST;
                    goto Path;
                case "put":
                    req.Verb = Verb.PUT;
                    goto Path;
                case "delete":
                    req.Verb = Verb.DELETE;
                    goto Path;
                case "accept":
                    req.Accept = value;
                    return;
                case "accept-language":
                    req.AcceptLanguage = value;
                    return;
                case "user-agent":
                    req.UserAgent = value;
                    return;
                case "accept-encoding":
                    req.AcceptEncoding = value;
                    return;
                case "host":
                    req.Host = value;
                    return;
                case "connection":
                    req.Connection = value;
                    return;
                case "accept-charset":
                    req.AcceptCharset = value;
                    return;
                default: return;
            }

            Path:
                string[] vals = value.Split(' ');
                req.Path = vals.Length > 0 ? vals[0] : value;
        }

        private struct Responder
        {
            public Callback Callback;
            public string Expression;
            public Verb Verb;

            public Responder(Verb verb, string expression, Callback callback)
            {
                Verb = verb;
                Expression = expression;
                Callback = callback;
            }

            public bool IsEmpty() { return String.IsNullOrEmpty(Expression); }
        }
    }

    public enum Verb
    {
        GET,
        POST,
        PUT,
        DELETE
    }

    public class Request
    {
        public Verb Verb { get; set; }
        public string Path { get; set; }
        public string Accept { get; set; }
        public string AcceptLanguage { get; set; }
        public string UserAgent { get; set; }
        public string AcceptEncoding { get; set; }
        public string Host { get; set; }
        public string Connection { get; set; }
        public string AcceptCharset { get; set; }
    }

    public class Response
    {
        private Socket socket { get; set; }

        public string ServerName { get; set; }
        public byte[] Body { get; set; }
        public int StatusCode { get; set; }
        public string HttpVersion { get; set; }
        public string ContentType { get; set; }

        public Response()
        {
            ServerName = JustServe.SERVERNAME;
            HttpVersion = "HTTP/1.1";
        }

        public Response(Socket Socket) : this()
        {
            this.socket = Socket;
        }

        public Response(Socket Socket, int StatusCode) : this(Socket)
        {
            this.StatusCode = StatusCode;
        }

        public void End(string body)
        {
            if (String.IsNullOrEmpty(ContentType))
                ContentType = Regex.IsMatch(body, "<(.)+>") ? "text/html" : "text/plain";

            Body = Encoding.UTF8.GetBytes(body);
            End(Body);
        }

        public void End(byte[] buffer)
        {
            if (String.IsNullOrEmpty(ContentType))
                ContentType = "text/html";

            string header = "{0}{1}\r\n"
                + "Server: {2}\r\n"
                + "Content-Type: {3}\r\n"
                + "Accept-Ranges: bytes\r\n"
                + "Content-Length: {4}\r\n\r\n";
            header = String.Format(header, HttpVersion, StatusCode, ServerName, ContentType, buffer.Length);
            socket.Send(Encoding.UTF8.GetBytes(header));
            socket.Send(buffer);
        }
    }
}
