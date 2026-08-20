using System;
namespace Xilium.CefGlue.Common.Shared
{
    internal abstract class CommonCefApp : CefApp
    {
        private readonly CustomScheme[] _customSchemes;

        internal CommonCefApp(CustomScheme[] customSchemes = null)
        {
            _customSchemes = customSchemes;
        }

        protected override void OnRegisterCustomSchemes(CefSchemeRegistrar registrar)
        {
            if (_customSchemes != null)
            {
                foreach (var scheme in _customSchemes)
                {
                    if (!registrar.AddCustomScheme(scheme.SchemeName, scheme.Options))
                    {
                        throw new InvalidOperationException(
                            $"Failed to register CEF custom scheme '{scheme.SchemeName}'.");
                    }
                }
            }
        }
    }
}
