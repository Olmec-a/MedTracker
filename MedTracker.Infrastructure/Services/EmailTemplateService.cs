using System.Net;
using MedTracker.Application.Interfaces;

namespace MedTracker.Infrastructure.Services;

/// <summary>
/// Простые inline-шаблоны на старте.
/// Когда понадобится локализация / красивый дизайн — перейти на Razor (RazorLight) или Fluid.
/// </summary>
public class EmailTemplateService : IEmailTemplateService
{
    public EmailTemplate RenderConfirmation(string fullName, string confirmationUrl)
    {
        var safeName = WebUtility.HtmlEncode(fullName);
        var safeUrl = WebUtility.HtmlEncode(confirmationUrl);

        var html = $$"""
            <!DOCTYPE html>
            <html>
            <body style="font-family: Arial, sans-serif; color: #1f2937; max-width: 560px; margin: 0 auto;">
              <h2 style="color: #2563eb;">Подтверждение регистрации</h2>
              <p>Здравствуйте, {{safeName}}!</p>
              <p>Спасибо за регистрацию в MedTracker. Чтобы активировать аккаунт, перейдите по ссылке:</p>
              <p>
                <a href="{{safeUrl}}"
                   style="display:inline-block; background:#2563eb; color:#fff;
                          padding:12px 24px; text-decoration:none; border-radius:6px;">
                   Подтвердить email
                </a>
              </p>
              <p style="color:#6b7280; font-size:14px;">
                Или скопируйте ссылку: <br/><span style="word-break:break-all;">{{safeUrl}}</span>
              </p>
              <p style="color:#6b7280; font-size:14px;">Ссылка действительна 24 часа.</p>
              <hr style="border:none; border-top:1px solid #e5e7eb; margin:24px 0;"/>
              <p style="color:#9ca3af; font-size:12px;">
                Если вы не регистрировались в MedTracker — просто проигнорируйте это письмо.
              </p>
            </body>
            </html>
            """;

        var plain = $"""
            Здравствуйте, {fullName}!

            Спасибо за регистрацию в MedTracker. Чтобы активировать аккаунт, перейдите по ссылке:
            {confirmationUrl}

            Ссылка действительна 24 часа.

            Если вы не регистрировались — проигнорируйте это письмо.
            """;

        return new EmailTemplate("Подтвердите email — MedTracker", html, plain);
    }

    public EmailTemplate RenderPasswordReset(string fullName, string resetUrl)
    {
        var safeName = WebUtility.HtmlEncode(fullName);
        var safeUrl = WebUtility.HtmlEncode(resetUrl);

        var html = $$"""
            <!DOCTYPE html>
            <html>
            <body style="font-family: Arial, sans-serif; color: #1f2937; max-width: 560px; margin: 0 auto;">
              <h2 style="color: #dc2626;">Сброс пароля</h2>
              <p>Здравствуйте, {{safeName}}!</p>
              <p>Вы (или кто-то от вашего имени) запросили сброс пароля для аккаунта MedTracker.
                 Чтобы установить новый пароль, перейдите по ссылке:</p>
              <p>
                <a href="{{safeUrl}}"
                   style="display:inline-block; background:#dc2626; color:#fff;
                          padding:12px 24px; text-decoration:none; border-radius:6px;">
                   Сбросить пароль
                </a>
              </p>
              <p style="color:#6b7280; font-size:14px;">Ссылка действительна 1 час.</p>
              <hr style="border:none; border-top:1px solid #e5e7eb; margin:24px 0;"/>
              <p style="color:#9ca3af; font-size:12px;">
                Если вы не запрашивали сброс пароля — игнорируйте это письмо. Ваш пароль не изменится.
              </p>
            </body>
            </html>
            """;

        var plain = $"""
            Здравствуйте, {fullName}!

            Вы запросили сброс пароля для аккаунта MedTracker. Чтобы установить новый пароль, перейдите:
            {resetUrl}

            Ссылка действительна 1 час.

            Если вы не запрашивали — проигнорируйте письмо.
            """;

        return new EmailTemplate("Сброс пароля — MedTracker", html, plain);
    }
}