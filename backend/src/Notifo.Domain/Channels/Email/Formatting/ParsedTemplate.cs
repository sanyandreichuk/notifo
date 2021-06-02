﻿// ==========================================================================
//  Notifo.io
// ==========================================================================
//  Copyright (c) Sebastian Stehle
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.ObjectPool;
using Notifo.Domain.UserNotifications;
using Notifo.Domain.Utils;
using Notifo.Infrastructure;

namespace Notifo.Domain.Channels.Email.Formatting
{
    public sealed class ParsedTemplate
    {
        private const string NotificationsPlaceholder = "<<<<Notifications>>>>";
        private const string ItemDefault = "NOTIFICATION";
        private const string ItemWithButton = "NOTIFICATION WITH BUTTON";
        private const string ItemWithButtonAndImage = "NOTIFICATION WITH BUTTON AND IMAGE";
        private const string ItemWithImage = "NOTIFICATION WITH IMAGE";
        private static readonly ObjectPool<StringBuilder> Pool = ObjectPool.Create(new StringBuilderPooledObjectPolicy());

        public string Text { get; set; }

        public Dictionary<string, string> ItemTemplates { get; set; } = new Dictionary<string, string>();

        public string Format(IEnumerable<BaseUserNotification> notifications, Dictionary<string, string?> properties, bool asHtml, IImageFormatter imageFormatter)
        {
            var notificationProperties = new Dictionary<string, string?>();

            var stringBuilder = Pool.Get();
            try
            {
                var text = Text.Format(properties);

                notifications.Foreach((notification, index) =>
                {
                    var formatting = notification.Formatting;

                    var inner = string.Empty;

                    var hasButton = !string.IsNullOrWhiteSpace(formatting.ConfirmText) && !string.IsNullOrWhiteSpace(notification.ConfirmUrl);
                    var hasImage = !string.IsNullOrWhiteSpace(formatting.ImageSmall) || !string.IsNullOrWhiteSpace(formatting.ImageLarge);

                    if (hasButton && hasImage)
                    {
                        ItemTemplates.TryGetValue(ItemWithButtonAndImage, out inner);
                    }

                    if (hasButton && string.IsNullOrWhiteSpace(inner))
                    {
                        ItemTemplates.TryGetValue(ItemWithButton, out inner);
                    }

                    if (hasImage && string.IsNullOrWhiteSpace(inner))
                    {
                        ItemTemplates.TryGetValue(ItemWithImage, out inner);
                    }

                    if (string.IsNullOrWhiteSpace(inner))
                    {
                        inner = ItemTemplates[ItemDefault];
                    }

                    notificationProperties.Clear();
                    notificationProperties["notification.body"] = GetBody(notification.Formatting, asHtml);
                    notificationProperties["notification.confirmText"] = GetConfirmText(notification.Formatting);
                    notificationProperties["notification.confirmUrl"] = GetConfirmUrl(notification);
                    notificationProperties["notification.imageLarge"] = GetImageLarge(notification.Formatting, imageFormatter);
                    notificationProperties["notification.imageSmall"] = GetImageSmall(notification.Formatting, imageFormatter);
                    notificationProperties["notification.subject"] = GetSubject(notification.Formatting, asHtml);

                    inner = inner.Format(notificationProperties);

                    stringBuilder.AppendLine(inner);

                    if (!string.IsNullOrEmpty(notification.TrackingUrl) && asHtml)
                    {
                        stringBuilder.Append($"<img height=\"0\" width=\"0\" src=\"{notification.TrackingUrl}\" />");
                    }
                });

                return text.Replace(NotificationsPlaceholder, stringBuilder.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Pool.Return(stringBuilder);
            }
        }

        private static string? GetConfirmText(NotificationFormatting<string> formatting)
        {
            return formatting.ConfirmText;
        }

        private static string? GetConfirmUrl(BaseUserNotification notification)
        {
            return notification.ConfirmUrl;
        }

        private static string? GetImageSmall(NotificationFormatting<string> formatting, IImageFormatter imageFormatter)
        {
            return imageFormatter.Format(formatting.ImageSmall, "EmailSmall");
        }

        private static string? GetImageLarge(NotificationFormatting<string> formatting, IImageFormatter imageFormatter)
        {
            return imageFormatter.Format(formatting.ImageLarge, "EmailSmall");
        }

        private static string GetSubject(NotificationFormatting<string> formatting, bool asHtml)
        {
            var subject = formatting.Subject!;

            if (asHtml && !string.IsNullOrWhiteSpace(formatting.LinkUrl))
            {
                subject = $"<a href=\"{formatting.LinkUrl}\" target=\"_blank\" rel=\"noopener\">{subject}</a>";
            }

            return subject;
        }

        private static string? GetBody(NotificationFormatting<string> formatting, bool asHtml)
        {
            var body = formatting.Body;

            if (asHtml && !string.IsNullOrWhiteSpace(formatting.LinkText) && !string.IsNullOrWhiteSpace(formatting.LinkUrl))
            {
                if (body?.Length > 0)
                {
                    return $"{body} <a href=\"{formatting.LinkUrl}\">{formatting.LinkText}</a>";
                }
                else
                {
                    return $"<a href=\"{formatting.LinkUrl}\">{formatting.LinkText}</a>";
                }
            }

            if (!string.IsNullOrWhiteSpace(formatting.LinkUrl))
            {
                if (body?.Length > 0)
                {
                    return $"{body} {formatting.LinkUrl}";
                }
                else
                {
                    return formatting.LinkUrl;
                }
            }

            return body;
        }

        public static ParsedTemplate? Create(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var result = new ParsedTemplate();

            result.Prepare(body);

            return result;
        }

        private void Prepare(string template)
        {
            while (true)
            {
                var (newTemplate, type, item) = Extract(template);

                if (item == null || type == null)
                {
                    break;
                }

                ItemTemplates[type.ToUpperInvariant()] = item;

                template = newTemplate;
            }

            if (!ItemTemplates.ContainsKey(ItemDefault))
            {
                throw new DomainException("Template must have a template for notifcations.");
            }

            Text = template;
        }

        private static (string Template, string? Type, string? Inner) Extract(string template)
        {
            var span = template.AsSpan();

            var start = Regex.Match(template, $"<!--[\\s]*START:(?<Type>.*)-->[\r\n]*", RegexOptions.IgnoreCase);

            if (!start.Success)
            {
                return (template, null, null);
            }

            var type = start.Groups["Type"].Value.Trim();

            var startOuter = start.Index;
            var startInner = startOuter + start.Length;

            var end = new Regex($"<!--[\\s]*END:[\\s]*{type}[\\s]*-->[\r\n]*", RegexOptions.IgnoreCase).Match(template, startOuter);

            if (!end.Success)
            {
                return (template, null, null);
            }

            var endInner = end.Index;
            var endOuter = endInner + end.Length;

            var stringBuilder = Pool.Get();
            try
            {
                var replacement = template.Contains(NotificationsPlaceholder) ? string.Empty : NotificationsPlaceholder;

                stringBuilder.Append(span.Slice(0, startOuter));
                stringBuilder.Append(replacement);
                stringBuilder.Append(span[endOuter..]);

                var inner = template[startInner..endInner];

                return (stringBuilder.ToString(), type, inner);
            }
            finally
            {
                Pool.Return(stringBuilder);
            }
        }
    }
}