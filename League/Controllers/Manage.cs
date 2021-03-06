using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axuno.Web;
using League.BackgroundTasks.Email;
using League.DI;
using League.Identity;
using League.Helpers;
using League.Models.ManageViewModels;
using League.TagHelpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using TournamentManager.DAL.EntityClasses;
using TournamentManager.DI;

namespace League.Controllers
{
    [Authorize]
    [Route("{organization:ValidOrganizations}/[controller]")]
    public class Manage : AbstractController
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IStringLocalizer<Manage> _localizer;
        private readonly Axuno.Tools.DateAndTime.TimeZoneConverter _timeZoneConverter;
        private readonly Axuno.BackgroundTask.IBackgroundQueue _queue;
        private readonly UserEmailTask _userEmailTask1;
        private readonly UserEmailTask _userEmailTask2;
        private readonly MetaDataHelper _metaData;
        private readonly SiteContext _siteContext;
        private readonly ILogger<Manage> _logger;
        private readonly PhoneNumberService _phoneNumberService;
        private readonly RegionInfo _regionInfo;

        public Manage(
            SignInManager<ApplicationUser> signInManager, IStringLocalizer<Manage> localizer,
            Axuno.Tools.DateAndTime.TimeZoneConverter timeZoneConverter,
            Axuno.BackgroundTask.IBackgroundQueue queue,
            UserEmailTask userEmailTask,
            MetaDataHelper metaData, SiteContext siteContext, 
            RegionInfo regionInfo, PhoneNumberService phoneNumberService,
            ILogger<Manage> logger)
        {
            _userManager = signInManager.UserManager;
            _signInManager = signInManager;
            _localizer = localizer;
            _timeZoneConverter = timeZoneConverter;
            _queue = queue;
            _userEmailTask1 = userEmailTask;
            _userEmailTask2 = userEmailTask.CreateNew();
            _metaData = metaData;
            _siteContext = siteContext;
            _phoneNumberService = phoneNumberService;
            _regionInfo = regionInfo;
            _logger = logger;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            //TournamentManager.Validators.UserEntityValidatorResource.Culture = CultureInfo.CurrentCulture;
            //var x = TournamentManager.EntityValidators.UserEntityValidatorResource.EmailMustBeSetAndValid;
            var ue = new TournamentManager.DAL.EntityClasses.UserEntity();

            ue.Fields[(int) TournamentManager.DAL.UserFieldIndex.UserName].Alias = _metaData.GetDisplayName<ChangeUsernameViewModel>(nameof(ChangeUsernameViewModel.Username));

            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return RedirectToAction(nameof(Account.SignIn), nameof(Account), new { Organization = _siteContext.UrlSegmentValue });
            }
            
            var model = new IndexViewModel(_timeZoneConverter)
            {
                ApplicationUser = user,
                HasPassword = await _userManager.HasPasswordAsync(user),
                Logins = await _userManager.GetLoginsAsync(user),
                ManageMessage = TempData.Get<ManageMessage>(nameof(ManageMessage))
            };
            // Display phone numbers in the format of the current region
            model.ApplicationUser.PhoneNumber = _phoneNumberService.Format(model.ApplicationUser.PhoneNumber,
                _regionInfo.TwoLetterISORegionName);
            model.ApplicationUser.PhoneNumber2 = _phoneNumberService.Format(model.ApplicationUser.PhoneNumber2,
                _regionInfo.TwoLetterISORegionName);

            return View(model);
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> ChangeUserName()
        {
            var model = new ChangeUsernameViewModel();
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                _logger.LogError($"Username '{HttpContext.User.Identity.Name}' not found in repository");
                ModelState.AddModelError(string.Empty, _localizer["'{0}' not found", _metaData.GetDisplayName<ChangeUsernameViewModel>(nameof(ChangeUsernameViewModel.Username))]);
                return PartialView(ViewNames.Manage._ChangeUsernameModalPartial, model);
            }

            model.Username = user.Name;
            return PartialView(ViewNames.Manage._ChangeUsernameModalPartial, model);
        }

        [HttpPost("[action]")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeUserName(ChangeUsernameViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return PartialView(ViewNames.Manage._ChangeUsernameModalPartial, model);
            }
            var user = await GetCurrentUserAsync();
            if (user != null)
            {
                if (user.NormalizedUserName == _userManager.KeyNormalizer.NormalizeName(model.Username))
                {
                    // do nothing and display success
                    TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.ChangeUsernameSuccess });
                    return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
                }
                user.UserName = model.Username;
                var result = await _userManager.UpdateAsync(user);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    _logger.LogInformation($"User '{user.Id}' changed the username successfully.");
                    TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.ChangeUsernameSuccess });
                    return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
                }
                AddErrors(result);
                //return View(model);
                return PartialView(ViewNames.Manage._ChangeUsernameModalPartial, model);
            }

            TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangeUsernameFailure });
            return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
        }

        

        [HttpGet("[action]")]
        public async Task<IActionResult> ChangeEmail()
        {
            var model = new ChangeEmailViewModel();
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                _logger.LogError($"User id '{GetCurrentUserId()}' not found in repository");
                ModelState.AddModelError(string.Empty, _localizer["'{0}' not found", _metaData.GetDisplayName<ChangeEmailViewModel>(nameof(ChangeEmailViewModel.Email))]);
                return PartialView(ViewNames.Manage._ChangeEmailModalPartial, model);
            }

            model.Email = user.Email;
            return PartialView(ViewNames.Manage._ChangeEmailModalPartial, model);
        }
 
        [HttpPost("[action]")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeEmail(ChangeEmailViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return PartialView(ViewNames.Manage._ChangeEmailModalPartial, model);
            }

            var user = await GetCurrentUserAsync();

            if (user == null)
            {
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangeEmailFailure });
                return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
            }

            if (user.NormalizedEmail == _userManager.KeyNormalizer.NormalizeEmail(model.Email))
            {
                _logger.LogInformation($"Current and new email are equal ('{model.Email}').");
                ModelState.AddModelError(nameof(ChangeEmailViewModel.Email), _localizer["Current and new email must be different"]);
                return PartialView(ViewNames.Manage._ChangeEmailModalPartial, model);
            }

            await SendEmail(user, EmailPurpose.NotifyCurrentPrimaryEmail, model.Email);
            await SendEmail(user, EmailPurpose.ConfirmNewPrimaryEmail, model.Email);

            TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.ChangeEmailConfirmationSent });
            return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
        }

        [AllowAnonymous]
        [HttpGet("[action]/{id}")]
        public async Task<IActionResult> ConfirmNewPrimaryEmail(string id, string code, string e)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangeEmailFailure });
                return RedirectToAction(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue });
            }

            // ChangeEmailAsync also sets EmailConfirmed = true
            var result = await _userManager.ChangeEmailAsync(user, e.Base64UrlDecode(), code.Base64UrlDecode());
            if (result != IdentityResult.Success)
            {
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangeEmailFailure });
                return RedirectToAction(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue });
            }

            TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.ChangeEmailSuccess });
            return RedirectToAction(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue });
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> EditEmail2()
        {
            var model = new EditEmail2ViewModel();
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                _logger.LogError($"User id '{GetCurrentUserId()}' not found in repository");
                ModelState.AddModelError(string.Empty, _localizer["'{0}' not found", _metaData.GetDisplayName<EditEmail2ViewModel>(nameof(EditEmail2ViewModel.Email2))]);
                return PartialView(ViewNames.Manage._EditEmail2ModalPartial, model);
            }

            model.Email2 = user.Email2;
            return PartialView(ViewNames.Manage._EditEmail2ModalPartial, model);
        }

        [HttpPost("[action]")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEmail2(EditEmail2ViewModel model, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return PartialView(ViewNames.Manage._EditEmail2ModalPartial, model);
            }

            var userEntity = await _siteContext.AppDb.UserRepository.GetLoginUserByUserNameAsync(User.Identity.Name, cancellationToken);
            if (userEntity == null)
            {
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangeEmail2Failure });
                return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
            }

            if (_userManager.KeyNormalizer.NormalizeEmail(userEntity.Email2) == _userManager.KeyNormalizer.NormalizeEmail(model.Email2))
            {
                // do nothing and display success
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.ChangeEmail2Success });
                return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
            }

            if (_userManager.KeyNormalizer.NormalizeEmail(userEntity.Email) == _userManager.KeyNormalizer.NormalizeEmail(model.Email2))
            {
                _logger.LogInformation($"Primary and additional email are equal ('{userEntity.Email}').");
                ModelState.AddModelError(nameof(EditEmail2ViewModel.Email2), _localizer["'{0}' and '{1}' must be different", _metaData.GetDisplayName<EditEmail2ViewModel>(nameof(EditEmail2ViewModel.Email2)), _metaData.GetDisplayName<ChangeEmailViewModel>(nameof(ChangeEmailViewModel.Email))]);
                return PartialView(ViewNames.Manage._EditEmail2ModalPartial, model);
            }

            userEntity.Email2 = model.Email2 ?? string.Empty;
            try
            {
                await _siteContext.AppDb.GenericRepository.SaveEntityAsync(userEntity, false, false, cancellationToken);
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.ChangeEmail2Success });
                return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
            }
            catch (Exception e)
            {
                _logger.LogError($"Save user name '{userEntity.UserName}' failed", e);
                return PartialView(ViewNames.Manage._EditEmail2ModalPartial, model);
            }
        }

        [HttpGet("[action]")]
        public IActionResult ChangePassword()
        {
            return PartialView(ViewNames.Manage._ChangePasswordModalPartial);
        }

        [HttpPost("[action]")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return PartialView(ViewNames.Manage._ChangePasswordModalPartial, model);
            }
            var user = await GetCurrentUserAsync();
            if (user != null)
            {
                if (!await _userManager.CheckPasswordAsync(user, model.CurrentPassword))
                {
                    // Tell the user, which password field is invalid
                    ModelState.AddModelError(nameof(ChangePasswordViewModel.CurrentPassword), _userManager.ErrorDescriber.PasswordMismatch().Description);
                    return PartialView(ViewNames.Manage._ChangePasswordModalPartial, model);
                }

                var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    _logger.LogInformation($"User '{user.Id}' changed the password successfully.");
                    TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.ChangePasswordSuccess });
                    return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
                }
                AddErrors(result);
                return PartialView(ViewNames.Manage._ChangePasswordModalPartial, model);
            }

            TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangePasswordFailure });
            return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
        }

        [HttpGet("[action]")]
        public IActionResult SetPassword()
        {
            return PartialView(ViewNames.Manage._SetPasswordModalPartial);
        }

        [HttpPost("[action]")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return PartialView(ViewNames.Manage._SetPasswordModalPartial, model);
            }

            var user = await GetCurrentUserAsync();
            if (user != null)
            {
                var result = await _userManager.AddPasswordAsync(user, model.NewPassword);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.SetPasswordSuccess });
                    return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
                }
                AddErrors(result);
                return PartialView(ViewNames.Manage._SetPasswordModalPartial, model);
            }

            TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.SetPasswordFailure });
            return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> EditPersonalDetails(CancellationToken cancellationToken)
        {
            var model = new PersonalDetailsViewModel();
            var user = await _siteContext.AppDb.UserRepository.GetLoginUserByUserNameAsync(HttpContext.User.Identity.Name, cancellationToken);
            if (user == null)
            {
                _logger.LogError($"User id '{GetCurrentUserId()}' not found in repository");
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangePersonalDetailsFailure });
                ModelState.AddModelError(string.Empty, _localizer["Personal details not found"]);
                return PartialView(ViewNames.Manage._EditPersonalDetailsModalPartial, model);
            }

            model.Gender = user.Gender;
            model.FirstName = user.FirstName;
            model.LastName = user.LastName;
            model.Nickname = user.Nickname;

            return PartialView(ViewNames.Manage._EditPersonalDetailsModalPartial, model);
        }

        [HttpPost("[action]")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPersonalDetails(PersonalDetailsViewModel model, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return PartialView(ViewNames.Manage._EditPersonalDetailsModalPartial, model);
            }

            var user = await _siteContext.AppDb.UserRepository.GetLoginUserByUserNameAsync(HttpContext.User.Identity.Name, cancellationToken);
            if (user == null)
            {
                _logger.LogError($"Username '{HttpContext.User.Identity.Name}' not found in repository");
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangePersonalDetailsFailure });
                return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
            }

            user.Gender = model.Gender;
            user.FirstName = model.FirstName;
            user.LastName = model.LastName;
            user.Nickname = model.Nickname ?? string.Empty;
            try
            {
                await _siteContext.AppDb.GenericRepository.SaveEntityAsync(user, false, false, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError($"Failure saving personal data for user id '{GetCurrentUserId()}'", e);
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangePersonalDetailsFailure });
                return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
            }

            _logger.LogInformation($"Personal data for user id '{GetCurrentUserId()}' updated");
            TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.ChangePersonalDetailsSuccess });
            return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> EditPhoneNumber(CancellationToken cancellationToken)
        {
            var model = new EditPhoneViewModel();
            var user = await _siteContext.AppDb.UserRepository.GetLoginUserByUserNameAsync(HttpContext.User.Identity.Name, cancellationToken);
            if (user == null)
            {
                _logger.LogError($"Username '{HttpContext.User.Identity.Name}' not found in repository");
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangePhoneFailure });
                ModelState.AddModelError(string.Empty, _localizer["Primary phone number not found"]);
                return PartialView(ViewNames.Manage._EditPhoneModalPartial, model);
            }

            model.PhoneNumber = _phoneNumberService.Format(user.PhoneNumber, _regionInfo.TwoLetterISORegionName);
            return PartialView(ViewNames.Manage._EditPhoneModalPartial, model);
        }

        [HttpPost("[action]")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPhoneNumber(EditPhoneViewModel model, CancellationToken cancellationToken)
        {
            UserEntity userEntity = null;

            async Task<IActionResult> Save()
            {
                // Save in unformatted, international format
                userEntity.PhoneNumber = _phoneNumberService.FormatForStorage(model.PhoneNumber, _regionInfo.TwoLetterISORegionName);

                try
                {
                    await _siteContext.AppDb.GenericRepository.SaveEntityAsync(userEntity, false, false, cancellationToken);
                }
                catch (Exception e)
                {
                    _logger.LogError($"Save user name '{userEntity.UserName}' failed", e);
                    TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangePhoneFailure });
                    return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
                }

                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.ChangePhoneSuccess });
                return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
            }

            if (!ModelState.IsValid)
            {
                return PartialView(ViewNames.Manage._EditPhoneModalPartial, model);
            }
            ModelState.Clear();

            userEntity = await _siteContext.AppDb.UserRepository.GetLoginUserByUserNameAsync(User.Identity.Name, cancellationToken);

            if (userEntity == null)
            {
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangePhoneFailure });
                return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
            }

            // Remove the phone number
            if (string.IsNullOrEmpty(model.PhoneNumber))
            {
                model.PhoneNumber = string.Empty;
                return await Save();
            }

            if (!string.IsNullOrWhiteSpace(model.PhoneNumber))
            {
                var validator = new TournamentManager.ModelValidators.PhoneNumberValidator(model.PhoneNumber, (_phoneNumberService, _regionInfo));
                await validator.CheckAsync(cancellationToken);
                validator.GetFailedFacts().ForEach(f => ModelState.AddModelError(nameof(model.PhoneNumber), f.Message));
            }

            if (!ModelState.IsValid)
            {
                return PartialView(ViewNames.Manage._EditPhoneModalPartial, model);
            }

            if (_phoneNumberService.IsMatch(userEntity.PhoneNumber, model.PhoneNumber, _regionInfo.TwoLetterISORegionName))
            {
                _logger.LogInformation($"Current and new primary phone number are equal ('{userEntity.PhoneNumber}').");
                ModelState.AddModelError(nameof(EditPhoneViewModel.PhoneNumber), _localizer["Current and new primary phone number must be different"]);
                return PartialView(ViewNames.Manage._EditPhoneModalPartial, model);
            }

            if (_phoneNumberService.IsMatch(userEntity.PhoneNumber2, model.PhoneNumber, _regionInfo.TwoLetterISORegionName))
            {
                _logger.LogInformation($"Primary and additional phone number are equal ('{userEntity.PhoneNumber}').");
                ModelState.AddModelError(nameof(EditPhone2ViewModel.PhoneNumber2), _localizer["'{0}' and '{1}' must be different", _metaData.GetDisplayName<EditPhoneViewModel>(nameof(EditPhoneViewModel.PhoneNumber)), _metaData.GetDisplayName<EditPhone2ViewModel>(nameof(EditPhone2ViewModel.PhoneNumber2))]);
                return PartialView(ViewNames.Manage._EditPhoneModalPartial, model);
            }

            return await Save();
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> EditPhoneNumber2(CancellationToken cancellationToken)
        {
            var model = new EditPhone2ViewModel();
            var user = await _siteContext.AppDb.UserRepository.GetLoginUserByUserNameAsync(HttpContext.User.Identity.Name, cancellationToken);
            if (user == null)
            {
                _logger.LogError($"Username '{HttpContext.User.Identity.Name}' not found in repository");
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangePhone2Failure });
                ModelState.AddModelError(string.Empty, _localizer["'{0}' not found", _metaData.GetDisplayName<EditPhone2ViewModel>(nameof(EditPhone2ViewModel.PhoneNumber2))]);
                return PartialView(ViewNames.Manage._EditPhone2ModalPartial, model);
            }

            model.PhoneNumber2 = _phoneNumberService.Format(user.PhoneNumber2, _regionInfo.TwoLetterISORegionName);
            return PartialView(ViewNames.Manage._EditPhone2ModalPartial, model);
        }

        [HttpPost("[action]")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPhoneNumber2(EditPhone2ViewModel model, CancellationToken cancellationToken)
        {
            UserEntity userEntity = null;

            async Task<IActionResult> Save()
            {
                // Save in unformatted, international format
                userEntity.PhoneNumber2 = _phoneNumberService.FormatForStorage(model.PhoneNumber2, _regionInfo.TwoLetterISORegionName);
                try
                {
                    await _siteContext.AppDb.GenericRepository.SaveEntityAsync(userEntity, false, false, cancellationToken);
                }
                catch (Exception e)
                {
                    _logger.LogError($"Save user name '{userEntity.UserName}' failed", e);
                    TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangePhone2Failure });
                    return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
                }

                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.ChangePhone2Success });
                return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
            }

            if (!ModelState.IsValid)
            {
                return PartialView(ViewNames.Manage._EditPhone2ModalPartial, model);
            }
            ModelState.Clear();

            userEntity = await _siteContext.AppDb.UserRepository.GetLoginUserByUserNameAsync(User.Identity.Name, cancellationToken);

            if (userEntity == null)
            {
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ChangePhone2Failure });
                return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
            }

            // Remove the phone number
            if (string.IsNullOrEmpty(model.PhoneNumber2))
            {
                model.PhoneNumber2 = string.Empty;
                return await Save();
            }
            
            if (!string.IsNullOrWhiteSpace(model.PhoneNumber2))
            {
                var validator = new TournamentManager.ModelValidators.PhoneNumberValidator(model.PhoneNumber2, (_phoneNumberService, _regionInfo));
                await validator.CheckAsync(cancellationToken);
                validator.GetFailedFacts().ForEach(f => ModelState.AddModelError(nameof(model.PhoneNumber2), f.Message));
            }

            if (!ModelState.IsValid)
            {
                return PartialView(ViewNames.Manage._EditPhone2ModalPartial, model);
            }

            if (_phoneNumberService.IsMatch(userEntity.PhoneNumber, model.PhoneNumber2, _regionInfo.TwoLetterISORegionName))
            {
                _logger.LogInformation($"Primary and additional phone number are equal ('{userEntity.PhoneNumber}').");
                ModelState.AddModelError(nameof(EditPhone2ViewModel.PhoneNumber2), _localizer["'{0}' and '{1}' must be different", _metaData.GetDisplayName<EditPhone2ViewModel>(nameof(EditPhone2ViewModel.PhoneNumber2)), _metaData.GetDisplayName<EditPhoneViewModel>(nameof(EditPhoneViewModel.PhoneNumber))]);
                return PartialView(ViewNames.Manage._EditPhone2ModalPartial, model);
            }

            return await Save();
        }

        //GET: /Manage/ManageLogins
        [HttpGet("[action]")]
        public async Task<IActionResult> ManageLogins()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.ManageLoginFailure });
                return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
            }
            var userLogins = await _userManager.GetLoginsAsync(user);
            var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
            var otherLogins = schemes.Where(auth => userLogins.All(ul => auth.Name != ul.LoginProvider)).ToList();
            return PartialView(ViewNames.Manage._ManageLoginsModalPartial, new ManageLoginsViewModel
            {
                CurrentLogins = userLogins,
                ShowRemoveButton = !string.IsNullOrEmpty(user.PasswordHash) || userLogins.Count > 1,
                OtherLogins = otherLogins
            });
        }

        [HttpPost("[action]")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveLogin(RemoveLoginViewModel account)
        {
            var user = await GetCurrentUserAsync();
            if (user != null)
            {
                var result = await _userManager.RemoveLoginAsync(user, account.LoginProvider, account.ProviderKey);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.RemoveLoginSuccess });
                    return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
                }
            }

            TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.RemoveLoginFailure });
            return JsonAjaxRedirectForModal(Url.Action(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue }));
        }


        [HttpPost("[action]")]
        [ValidateAntiForgeryToken]
        public IActionResult LinkLogin(string provider)
        {
            // Request a redirect to the external login provider to link a login for the current user
            var redirectUrl = Url.Action(nameof(LinkLoginCallback), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, _userManager.GetUserId(User));
            return Challenge(properties, provider);
        }

        [HttpGet("[action]")]
        public async Task<ActionResult> LinkLoginCallback()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.AddLoginFailure });
                return RedirectToAction(nameof(Index));
            }
            var info = await _signInManager.GetExternalLoginInfoAsync(await _userManager.GetUserIdAsync(user));
            if (info == null)
            {
                TempData.Put<ManageMessage>(nameof(ManageMessage), new ManageMessage { AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.AddLoginFailure });
                return RedirectToAction(nameof(Index), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue });
            }
            var result = await _userManager.AddLoginAsync(user, info);
            TempData.Put<ManageMessage>(nameof(ManageMessage), result.Succeeded
                ? new ManageMessage
                    {AlertType = SiteAlertTagHelper.AlertType.Success, MessageId = MessageId.AddLoginSuccess}
                : new ManageMessage
                    {AlertType = SiteAlertTagHelper.AlertType.Danger, MessageId = MessageId.AddLoginFailure});

            return RedirectToAction(nameof(Index),nameof(Manage), new { Organization = _siteContext.UrlSegmentValue });
        }

        [HttpGet("[action]")]
        public ActionResult DeleteAccount()
        {
            return PartialView(ViewNames.Manage._DeleteAccountModalPartial);
        }

        [HttpPost("[action]")]
        public async Task<ActionResult> DeleteAccountConfirmed()
        {
            var user = await GetCurrentUserAsync();
            if (user != null)
            {
                var result = await _userManager.DeleteAsync(user);
                if (!result.Succeeded)
                {
                    _logger.LogError($"Account for username '{user.Id}' could not be deleted.");
                }
            }
            await _signInManager.SignOutAsync();
            return JsonAjaxRedirectForModal("/");
        }

        #region *** Helpers ***

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        private enum EmailPurpose
        {
            /// <summary>
            /// Send a notification to the current primary email, that a request to change is pending
            /// </summary>
            NotifyCurrentPrimaryEmail,
            /// <summary>
            /// Email with a code to change the primary email
            /// </summary>
            ConfirmNewPrimaryEmail
        }

        private async Task<ApplicationUser> GetCurrentUserAsync()
        {
            return await _userManager.GetUserAsync(HttpContext.User);
        }

        /// <summary>
        /// Sends an email for the given <see cref="Account.EmailPurpose"/> to the <see cref="ApplicationUser"/>
        /// </summary>
        /// <param name="user">The <see cref="ApplicationUser"/> as the recipient of the email.</param>
        /// <param name="purpose">The <see cref="Account.EmailPurpose"/> of the email.</param>
        /// <param name="model">The model parameter for the view.</param>
        private async Task SendEmail(ApplicationUser user, EmailPurpose purpose, string model)
        {
            _userEmailTask1.Timeout = _userEmailTask2.Timeout = TimeSpan.FromMinutes(1);
            _userEmailTask1.EmailCultureInfo = _userEmailTask2.EmailCultureInfo = CultureInfo.CurrentUICulture;
            
            switch (purpose)
            {
                case EmailPurpose.NotifyCurrentPrimaryEmail:
                    _userEmailTask1.ToEmail = user.Email;
                    _userEmailTask1.Subject = _localizer["Your primary email is about to be changed"].Value;
                    _userEmailTask1.ViewNames = new[] {ViewNames.Emails.NotifyCurrentPrimaryEmail, ViewNames.Emails.NotifyCurrentPrimaryEmailTxt };
                    _userEmailTask1.LogMessage = "Notify current primary email about the requested change";
                    _userEmailTask1.Model = (Email: model, CallbackUrl: string.Empty, OrganizationContext: _siteContext);
                    _queue.QueueTask(_userEmailTask1);
                    break;
                case EmailPurpose.ConfirmNewPrimaryEmail:
                    var newEmail = model;
                    _userEmailTask2.ToEmail = newEmail;
                    _userEmailTask2.Subject = _localizer["Please confirm your new primary email"].Value;
                    _userEmailTask2.ViewNames = new [] {ViewNames.Emails.ConfirmNewPrimaryEmail, ViewNames.Emails.ConfirmNewPrimaryEmailTxt };
                    _userEmailTask2.LogMessage = "Email to confirm the new primary email";
                    var code = (await _userManager.GenerateChangeEmailTokenAsync(user, newEmail)).Base64UrlEncode();
                    _userEmailTask2.Model = (Email: newEmail, CallbackUrl: Url.Action(nameof(ConfirmNewPrimaryEmail), nameof(Manage), new { Organization = _siteContext.UrlSegmentValue, id = user.Id, code, e = newEmail.Base64UrlEncode()}, protocol: HttpContext.Request.Scheme), OrganizationContext: _siteContext);
                    _queue.QueueTask(_userEmailTask2);
                    break;
                default:
                    _logger.LogError($"Illegal enum type for {nameof(EmailPurpose)}");
                    break;
            }
        }
        #endregion
    }
}
