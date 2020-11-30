﻿using HtmlAgilityPack;
using log4net;
using System;
using System.IO;
using System.Linq;
using System.Net;

namespace Doway.Tools.Robinhood
{
    public class WebsiteCopier
    {
        private static readonly ILog _logger = LogManager.GetLogger(typeof(WebsiteCopier));
        public WebsiteCopier(string url, string savePath) : this(new Uri(url), new DirectoryInfo(savePath)) { }
        public WebsiteCopier(Uri uri, DirectoryInfo saveTarget)
        {
            StartPoint = uri;
            TargetFolder = saveTarget;
        }
        public Uri StartPoint { get; private set; }
        public DirectoryInfo TargetFolder { get; private set; }
        public void StartCopy()
        {
            if (!TargetFolder.Exists) TargetFolder.Create();
            GrabNode(StartPoint);
        }
        private void GrabNode(Uri uri, bool deleteExist = true)
        {
            try
            {
                if (uri.Authority != StartPoint.Authority) return;

                var file_path = uri.LocalPath;
                if (file_path.EndsWith("/")) file_path += "index.html";
                file_path = file_path.Replace(".aspx", ".html").Replace("/", "\\");
                var file = new FileInfo(TargetFolder.FullName + file_path);
                if (!file.Directory.Exists) file.Directory.Create();
                if (deleteExist && file.Exists) file.Delete();
                file.Refresh();
                if (file.Exists) return;

                var req = WebRequest.CreateHttp(uri);
                using (var res = req.GetResponse())
                {
                    if (res.ContentType.Contains("text"))
                    {
                        string content = null;
                        using (var sr = new StreamReader(res.GetResponseStream()))
                        {
                            content = sr.ReadToEnd();
                            using (var sw = file.CreateText())
                                sw.Write(content.Replace(".aspx", ".html"));
                        }
                        if(file.Extension.ToLower() == ".css")
                        {
                            var begin = content.IndexOf("url(");
                            while (begin > 0)
                            {
                                var end = content.IndexOf(")", begin + "url(".Length);
                                var length = end - (begin + "url(".Length);
                                var url = content.Substring(begin + "url(".Length, length);
                                if (!url.StartsWith("http")) url = Combine(uri, url);
                                GrabNode(new Uri(url));

                                begin = content.IndexOf("url(", end);
                            }
                        }
                        if (res.ContentType.Contains("text/html"))
                        {
                            var s = uri.AbsolutePath.Split('/');
                            var tmp = uri.Scheme + "://" + uri.Authority + string.Join("/", s.Take(s.Length - 1).ToArray());
                            uri = new Uri(tmp);
                            var doc = new HtmlDocument();
                            doc.LoadHtml(content);
                            foreach (var node in doc.DocumentNode.ChildNodes)
                                Handle(node, uri);
                        }
                    }
                    else
                    {
                        using (var fs = file.Create())
                            res.GetResponseStream().CopyTo(fs);
                    }
                }
            }
            catch (WebException ex)
            {
                if (ex.Message.Contains("404") || ex.Message.Contains("403"))
                    _logger.Warn(ex.Message + "[" + uri + "]");
                else
                {
                    _logger.Error(ex.Message + "[" + uri + "]", ex);
                }
            }
            catch(Exception ex)
            {
                _logger.Error(ex.Message + "[" + uri + "]", ex);
            }
        }
        private static string Combine(Uri uri, string path)
        {
            path = path.Replace("\r\n", "").Trim();
            if (path.StartsWith("/"))
                return uri.Scheme + "://" + uri.Authority.Trim() + path;

            return uri.AbsoluteUri.Trim() + "/" + path;
        }
        private void Handle(HtmlNode node, Uri currentUri)
        {
            _logger.DebugFormat("node(type={0} / name={1} / currentUri={2})", node.GetType(), node.Name, currentUri);
            try
            {
                switch (node.Name.ToLower())
                {
                    case "a":
                    case "link":
                        {
                            var url = node.Attributes["href"]?.Value;
                            if (!string.IsNullOrWhiteSpace(url) && !url.Contains("javascript"))
                            {
                                if (!url.StartsWith("http")) url = Combine(currentUri, url);
                                GrabNode(new Uri(url));
                            }
                        }
                        break;
                    case "img":
                    case "script":
                        {
                            var url = node.Attributes["src"]?.Value;
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                if (!url.StartsWith("http")) url = Combine(currentUri, url);
                                GrabNode(new Uri(url));
                            }
                        }
                        break;
                }
            }
            catch (UriFormatException e)
            {
                _logger.Error(node.OuterHtml + " " + e.Message, e);
            }
            foreach (var subNode in node.ChildNodes)
                Handle(subNode, currentUri);
        }
    }
}
