using System;
using System.Collections.Generic;
using System.Linq;
using JabbR.ContentProviders.Core;
using JabbR.Infrastructure;
using JabbR.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace JabbR.Controllers
{
    [Route("administration")]
    [Authorize(Policy = "AdminOnly")]
    public class AdministrationController : Controller
    {
        private readonly ApplicationSettings _applicationSettings;
        private readonly ISettingsManager _settingsManager;
        private readonly IEnumerable<IContentProvider> _contentProviders;

        public AdministrationController(
            ApplicationSettings applicationSettings,
            ISettingsManager settingsManager,
            IEnumerable<IContentProvider> contentProviders)
        {
            _applicationSettings = applicationSettings;
            _settingsManager = settingsManager;
            _contentProviders = contentProviders;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var allContentProviders = _contentProviders
                .OrderBy(provider => provider.GetType().Name)
                .ToList();
            var model = new
            {
                AllContentProviders = allContentProviders,
                ApplicationSettings = _applicationSettings
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index([FromForm] ApplicationSettings settings, [FromForm] List<string> enabledContentProviders)
        {
            if (!ModelState.IsValid)
            {
                return View(_applicationSettings);
            }

            try
            {
                // Filter out empty/null providers
                settings.ContentProviders = settings.ContentProviders
                    .Where(cp => !string.IsNullOrEmpty(cp.Name))
                    .ToList();

                // We posted the enabled ones, but we store the disabled ones. Flip it around...
                settings.DisabledContentProviders =
                    new HashSet<string>(_contentProviders
                        .Select(cp => cp.GetType().Name)
                        .Where(typeName => enabledContentProviders == null ||
                            !enabledContentProviders.Contains(typeName))
                        .ToList());

                if (ApplicationSettings.TryValidateSettings(settings, out IDictionary<string, string> errors))
                {
                    _settingsManager.Save(settings);
                    TempData["SuccessMessage"] = LanguageResources.SettingsSaveSuccess;
                    return RedirectToAction(nameof(Index));
                }
                else
                {
                    foreach (var error in errors)
                    {
                        ModelState.AddModelError(error.Key, error.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
            }

            return View(_applicationSettings);
        }
    }
}