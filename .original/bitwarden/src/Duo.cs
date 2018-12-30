// Copyright (C) 2018 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Bitwarden
{
    internal static class Duo
    {
        // Returns the second factor token from Duo or blank when canceled by the user.
        public static string Authenticate(Response.InfoDuo info, Ui ui, IHttpClient http)
        {
            var jsonHttp = new JsonHttpClient(http, $"https://{info.Host}");

            var signature = ParseSignature(info.Signature);
            var html = DownloadFrame(info.Host, signature.Tx, http);
            var frame = ParseFrame(html);

            while (true)
            {
                // Ask the user to choose what to do
                var (device, factor) = ui.ChooseDuoFactor(frame.Devices);
                if (device == null)
                    return ""; // Canceled by user

                // SMS is a special case: it doesn't submit any codes, it rather tells the server to send
                // a new batch of passcodes to the phone via SMS.
                if (factor == Ui.DuoFactor.SendPasscodesBySms)
                {
                    SubmitFactor(device, factor, frame.Sid, "", jsonHttp);
                    factor = Ui.DuoFactor.Passcode;
                }

                // Ask for the passcode
                var passcode = "";
                if (factor == Ui.DuoFactor.Passcode)
                {
                    passcode = ui.ProvideDuoPasscode(device);
                    if (passcode.IsNullOrEmpty())
                        return ""; // Canceled by user
                }

                var token = SubmitFactorAndWaitForToken(device, factor, frame.Sid, passcode, ui, jsonHttp);

                // Flow error like an incorrect passcode. The UI has been updated with the error. Keep going.
                if (token.IsNullOrEmpty())
                    continue;

                // All good
                return $"{token}:{signature.App}";
            }
        }

        internal static (string Tx, string App) ParseSignature(string signature)
        {
            var parts = signature.Split(':');
            if (parts.Length != 2)
                throw MakeInvalidResponseError("Duo HTML: the signature is invalid or in an unsupported format");

            return (parts[0], parts[1]);
        }

        internal static HtmlDocument DownloadFrame(string host, string tx, IHttpClient http)
        {
            const string parent = "https%3A%2F%2Fvault.bitwarden.com%2F%23%2F2fa";
            const string version = "2.6";

            var url = $"https://{host}/frame/web/v1/auth?tx={tx}&parent={parent}&v={version}";
            return Parse(Post(url, http));
        }

        internal static string Post(string url, IHttpClient http)
        {
            try
            {
                return http.Post(url, "", new Dictionary<string, string>());
            }
            catch (WebException e)
            {
                throw MakeNetworkError("Network error occurred", e);
            }
        }

        internal static HtmlDocument Parse(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }

        internal static (string Sid, Ui.DuoDevice[] Devices) ParseFrame(HtmlDocument html)
        {
            // Find the main form
            var form = html.DocumentNode.SelectSingleNode("//form[@id='login-form']");
            if (form == null)
                throw MakeInvalidResponseError("Duo HTML: main form is not found");

            // Find all the devices and the signature
            var sid = GetInputValue(form, "sid");
            var devices = GetDevices(form);

            if (sid == null || devices == null)
                throw MakeInvalidResponseError("Duo HTML: signature or devices are not found");

            return (sid, devices);
        }

        // All the info is the frame is stored in input fields <input name="name" value="value">
        internal static string GetInputValue(HtmlNode form, string name)
        {
            return form
                .SelectSingleNode($"./input[@name='{name}']")?
                .Attributes["value"]?
                .DeEntitizeValue;
        }

        // Returns the transaction id
        internal static string SubmitFactor(Ui.DuoDevice device,
                                            Ui.DuoFactor factor,
                                            string sid,
                                            string passcode,
                                            JsonHttpClient jsonHttp)
        {
            var parameters = new Dictionary<string, string>
            {
                {"sid", sid},
                {"device", device.Id},
                {"factor", GetFactorParameterValue(factor)},
            };

            if (!passcode.IsNullOrEmpty())
                parameters["passcode"] = passcode;

            var response = PostForm("frame/prompt", parameters, jsonHttp);

            var txid = (string)response["response"]?["txid"];
            if (txid.IsNullOrEmpty())
                throw MakeInvalidResponseError("Duo: transaction ID (txid) is expected by wasn't found");

            return txid;
        }

        // Returns null when a recoverable flow error (like incorrect code or time out) happened
        // TODO: Don't return null, use something more obvious
        internal static string SubmitFactorAndWaitForToken(Ui.DuoDevice device,
                                                           Ui.DuoFactor factor,
                                                           string sid,
                                                           string passcode,
                                                           Ui ui,
                                                           JsonHttpClient jsonHttp)
        {
            var txid = SubmitFactor(device, factor, sid, passcode, jsonHttp);

            var url = PollForResultUrl(sid, txid, ui, jsonHttp);
            if (url.IsNullOrEmpty())
                return null;

            return FetchToken(sid, url, ui, jsonHttp);
        }

        // Returns null when a recoverable flow error (like incorrect code or time out) happened
        // TODO: Don't return null, use something more obvious
        internal static string PollForResultUrl(string sid, string txid, Ui ui, JsonHttpClient jsonHttp)
        {
            const int MaxPollAttempts = 100;

            // Normally it wouldn't poll nearly as many times. Just a few at most. It either bails on error or
            // returns the result. This number here just to prevent an infinite loop, while is never a good idea.
            for (var i = 0; i < MaxPollAttempts; i += 1)
            {
                var response = PostForm("frame/status",
                                        new Dictionary<string, string> {{"sid", sid}, {"txid", txid}},
                                        jsonHttp);

                var (status, text) = GetResponseStatus(response);
                UpdateUi(status, text, ui);

                switch (status)
                {
                case Ui.DuoStatus.Success:
                    var url = (string)response["response"]?["result_url"];
                    if (url.IsNullOrEmpty())
                        throw MakeInvalidResponseError("Duo: result URL (result_url) was expected but wasn't found");

                    // Done
                    return url;
                case Ui.DuoStatus.Error:
                    return null; // TODO: Use something better than null
                }
            }

            throw MakeInvalidResponseError("Duo: expected to receive a valid result or error, got none of it");
        }

        internal static string FetchToken(string sid, string url, Ui ui, JsonHttpClient jsonHttp)
        {
            var response = PostForm(url, new Dictionary<string, string> {{"sid", sid}}, jsonHttp);

            UpdateUi(response, ui);

            var token = (string)response["response"]?["cookie"];
            if (token.IsNullOrEmpty())
                throw MakeInvalidResponseError("Duo: authentication token expected in response but wasn't found");

            return token;
        }

        internal static JObject PostForm(string endpoint, Dictionary<string, string> parameters, JsonHttpClient jsonHttp)
        {
            var response = jsonHttp.PostForm(endpoint, parameters);

            // Something went wrong
            if ((string)response["stat"] != "OK")
                throw MakeRespondedWithError($"Duo: POST to {jsonHttp.BaseUrl}/{endpoint} failed", response);

            return response;
        }

        internal static void UpdateUi(JObject response, Ui ui)
        {
            var (status, text) = GetResponseStatus(response);
            UpdateUi(status, text, ui);
        }

        internal static void UpdateUi(Ui.DuoStatus status, string text, Ui ui)
        {
            if (text.IsNullOrEmpty())
                return;

            ui.UpdateDuoStatus(status, text);
        }

        internal static (Ui.DuoStatus Status, string Text) GetResponseStatus(JObject response)
        {
            var status = Ui.DuoStatus.Info;
            switch ((string)response["response"]?["result"])
            {
            case "SUCCESS":
                status = Ui.DuoStatus.Success;
                break;
            case "FAILURE":
                status = Ui.DuoStatus.Error;
                break;
            }

            var text = (string)response["response"]?["status"] ?? "";

            return (status, text);
        }

        // Extracts all devices listed in the login form.
        // Devices with no supported methods are ignored.
        internal static Ui.DuoDevice[] GetDevices(HtmlNode form)
        {
            var devices = form
                .SelectNodes("//select[@name='device']/option")?
                .Select(x => (Id: x.Attributes["value"]?.DeEntitizeValue,
                              Name: HtmlEntity.DeEntitize(x.InnerText ?? "")));

            if (devices == null || devices.Any(x => x.Id == null || x.Name == null))
                return null;

            return devices
                .Select(x => new Ui.DuoDevice(x.Id, x.Name, GetDeviceFactors(form, x.Id)))
                .Where(x => x.Factors.Length > 0)
                .ToArray();
        }

        // Extracts all the second factor methods supported by the device.
        // Unsupported methods are ignored.
        internal static Ui.DuoFactor[] GetDeviceFactors(HtmlNode form, string deviceId)
        {
            var sms = CanSendSmsToDevice(form, deviceId)
                ? new Ui.DuoFactor[] {Ui.DuoFactor.SendPasscodesBySms}
                : new Ui.DuoFactor[0];

            return form
                .SelectSingleNode($".//fieldset[@data-device-index='{deviceId}']")?
                .SelectNodes(".//input[@name='factor']")?
                .Select(x => x.Attributes["value"]?.DeEntitizeValue)?
                .Select(x => ParseFactor(x))?
                .Where(x => x != null)?
                .Select(x => x.Value)?
                .Concat(sms)?
                .ToArray() ?? new Ui.DuoFactor[0];
        }

        internal static bool CanSendSmsToDevice(HtmlNode form, string deviceId)
        {
            return form
                .SelectSingleNode($".//fieldset[@data-device-index='{deviceId}']")?
                .SelectSingleNode(".//input[@name='phone-smsable' and @value='true']") != null;
        }

        internal static Ui.DuoFactor? ParseFactor(string s)
        {
            switch (s)
            {
            case "Duo Push":
                return Ui.DuoFactor.Push;
            case "Phone Call":
                return Ui.DuoFactor.Call;
            case "Passcode":
                return Ui.DuoFactor.Passcode;
            }

            return null;
        }

        internal static string GetFactorParameterValue(Ui.DuoFactor factor)
        {
            switch (factor)
            {
            case Ui.DuoFactor.Push:
                return "Duo Push";
            case Ui.DuoFactor.Call:
                return "Phone Call";
            case Ui.DuoFactor.Passcode:
                return "Passcode";
            case Ui.DuoFactor.SendPasscodesBySms:
                return "sms";
            }

            return "";
        }

        internal static ClientException MakeNetworkError(string message, Exception original = null)
        {
            return new ClientException(ClientException.FailureReason.NetworkError, message, original);
        }

        internal static ClientException MakeInvalidResponseError(string message, Exception original = null)
        {
            return new ClientException(ClientException.FailureReason.InvalidResponse, message, original);
        }

        internal static ClientException MakeRespondedWithError(string message,
                                                               JObject response,
                                                               Exception original = null)
        {
            var serverMessage = (string)response["message"] ?? "none";
            return new ClientException(ClientException.FailureReason.RespondedWithError,
                                       $"{message} Server message: {serverMessage}",
                                       original);
        }
    }
}
