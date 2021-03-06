﻿using BioEngine.Core.Properties;
using BioEngine.Core.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace BioEngine.Core.Web
{
    public abstract class BaseController : Controller
    {
        protected ILogger Logger { get; }
        protected IStorage Storage { get; }
        protected PropertiesProvider PropertiesProvider { get; }
        protected LinkGenerator LinkGenerator { get; }
        
        protected string Version { get; }

        protected BaseController(BaseControllerContext context)
        {
            Logger = context.Logger;
            Storage = context.Storage;
            PropertiesProvider = context.PropertiesProvider;
            LinkGenerator = context.LinkGenerator;
            Version = context.Version;
        }
    }
}
