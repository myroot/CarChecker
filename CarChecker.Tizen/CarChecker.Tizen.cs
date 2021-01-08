using Microsoft.MobileBlazorBindings.WebView.Elements;
using Microsoft.MobileBlazorBindings.WebView.Tizen;
using System;
using System.IO;
using System.Reflection;
using Xamarin.Forms;

[assembly: ExportRenderer(typeof(WebViewExtended), typeof(WebKitWebViewRenderer))]

namespace CarChecker.Tizen
{
    class Program : global::Xamarin.Forms.Platform.Tizen.FormsApplication
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            System.IO.Directory.SetCurrentDirectory(global::Tizen.Applications.Application.Current.DirectoryInfo.Resource);
            LoadApplication(new HybridApp());
        }

        static void Main(string[] args)
        {
            var app = new Program();


            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var asmName = e.Name.Split(",")[0];
                var dllPath = System.IO.Path.Combine(app.ApplicationInfo.ExecutablePath, "../", asmName + ".dll");
                return File.Exists(dllPath) ? Assembly.LoadFile(dllPath) : null;
            };

            BlazorHybridTizen.Init();

            Forms.Init(app);
            app.Run(args);
        }
    }
}
