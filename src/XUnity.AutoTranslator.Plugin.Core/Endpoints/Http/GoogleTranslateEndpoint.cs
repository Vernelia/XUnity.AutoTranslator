﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using SimpleJSON;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Constants;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.AutoTranslator.Plugin.Core.Web;

namespace XUnity.AutoTranslator.Plugin.Core.Endpoints.Http
{
   internal class GoogleTranslateEndpoint : HttpEndpoint
   {
      private static readonly string HttpsServicePointTemplateUrl = "https://translate.googleapis.com/translate_a/single?client=t&dt=t&sl={0}&tl={1}&ie=UTF-8&oe=UTF-8&tk={2}&q={3}";
      private static readonly string HttpsTranslateUserSite = "https://translate.google.com";
      private static readonly string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/70.0.3538.102 Safari/537.36";
      private static readonly System.Random RandomNumbers = new System.Random();

      private static readonly string[] Accepts = new string[] { null, "*/*", "application/json" };
      private static readonly string[] AcceptLanguages = new string[] { null, "en-US,en;q=0.9", "en-US", "en" };
      private static readonly string[] Referers = new string[] { null, "https://translate.google.com/" };
      private static readonly string[] AcceptCharsets = new string[] { null, Encoding.UTF8.WebName };

      private static readonly string Accept = Accepts[ RandomNumbers.Next( Accepts.Length ) ];
      private static readonly string AcceptLanguage = AcceptLanguages[ RandomNumbers.Next( AcceptLanguages.Length ) ];
      private static readonly string Referer = Referers[ RandomNumbers.Next( Referers.Length ) ];
      private static readonly string AcceptCharset = AcceptCharsets[ RandomNumbers.Next( AcceptCharsets.Length ) ];

      private CookieContainer _cookieContainer;
      private bool _hasSetup = false;
      private long m = 427761;
      private long s = 1179739010;

      public GoogleTranslateEndpoint()
      {
         _cookieContainer = new CookieContainer();
      }

      public override string Id => KnownEndpointNames.GoogleTranslate;

      public override string FriendlyName => "Google! Translate";

      public override void Initialize( InitializationContext context )
      {
         context.HttpSecurity.EnableSslFor( "translate.google.com", "translate.googleapis.com" );
      }

      public override XUnityWebRequest CreateTranslationRequest( string untranslatedText, string from, string to )
      {
         var request = new XUnityWebRequest(
            string.Format(
               HttpsServicePointTemplateUrl,
               from,
               to,
               Tk( untranslatedText ),
               WWW.EscapeURL( untranslatedText ) ) );

         request.Cookies = _cookieContainer;
         AddHeaders( request, true );

         return request;
      }

      private XUnityWebRequest CreateWebSiteRequest()
      {
         var request = new XUnityWebRequest( HttpsTranslateUserSite );

         request.Cookies = _cookieContainer;
         AddHeaders( request, false );

         return request;
      }

      private void AddHeaders( XUnityWebRequest request, bool isTranslationRequest )
      {
         request.Headers[ HttpRequestHeader.UserAgent ] = string.IsNullOrEmpty( AutoTranslationState.UserAgent ) ? DefaultUserAgent : AutoTranslationState.UserAgent;
         if( AcceptLanguage != null )
         {
            request.Headers[ HttpRequestHeader.AcceptLanguage ] = AcceptLanguage;
         }
         if( Accept != null )
         {
            request.Headers[ HttpRequestHeader.Accept ] = Accept;
         }
         if( Referer != null && isTranslationRequest )
         {
            request.Headers[ HttpRequestHeader.Referer ] = Referer;
         }
         if( AcceptCharset != null )
         {
            request.Headers[ HttpRequestHeader.AcceptCharset ] = AcceptCharset;
         }
      }

      public override void InspectTranslationResponse( XUnityWebResponse response )
      {
         CookieCollection cookies = response.NewCookies;
         foreach( Cookie cookie in cookies )
         {
            // redirect cookie to correct domain
            cookie.Domain = ".googleapis.com";
         }

         // FIXME: Is this needed? Should already be added
         _cookieContainer.Add( cookies );
      }

      public override IEnumerator OnBeforeTranslate()
      {
         if( !_hasSetup || AutoTranslationState.TranslationCount % 100 == 0 )
         {
            _hasSetup = true;

            // Setup TKK and cookies
            var enumerator = SetupTKK();
            while( enumerator.MoveNext() )
            {
               yield return enumerator.Current;
            }
         }
      }

      public IEnumerator SetupTKK()
      {
         XUnityWebResponse response = null;

         _cookieContainer = new CookieContainer();

         try
         {
            var client = new XUnityWebClient();
            var request = CreateWebSiteRequest();
            response = client.Send( request );
         }
         catch( Exception e )
         {
            Logger.Current.Warn( e, "An error occurred while setting up GoogleTranslate TKK. Using fallback TKK values instead." );
            yield break;
         }

         if( Features.SupportsCustomYieldInstruction )
         {
            yield return response;
         }
         else
         {
            while( response.keepWaiting )
            {
               yield return new WaitForSeconds( 0.2f );
            }
         }

         InspectTranslationResponse( response );

         // failure
         if( response.Error != null )
         {
            Logger.Current.Warn( response.Error, "An error occurred while setting up GoogleTranslate TKK. Using fallback TKK values instead." );
            yield break;
         }

         // failure
         if( response.Result == null )
         {
            Logger.Current.Warn( null, "An error occurred while setting up GoogleTranslate TKK. Using fallback TKK values instead." );
            yield break;
         }

         try
         {
            var html = response.Result;

            bool found = false;
            string[] lookups = new[] { "tkk:'", "TKK='" };
            foreach( var lookup in lookups )
            {
               var index = html.IndexOf( lookup );
               if( index > -1 ) // simple string approach
               {
                  var startIndex = index + lookup.Length;
                  var endIndex = html.IndexOf( "'", startIndex );
                  var result = html.Substring( startIndex, endIndex - startIndex );

                  var parts = result.Split( '.' );
                  if( parts.Length == 2 )
                  {
                     m = long.Parse( parts[ 0 ] );
                     s = long.Parse( parts[ 1 ] );
                     found = true;
                     break;
                  }
               }
            }

            if( !found )
            {
               Logger.Current.Warn( "An error occurred while setting up GoogleTranslate TKK. Could not locate TKK value. Using fallback TKK values instead." );
            }
         }
         catch( Exception e )
         {
            Logger.Current.Warn( e, "An error occurred while setting up GoogleTranslate TKK. Using fallback TKK values instead." );
         }
      }

      // TKK Approach stolen from Translation Aggregator r190, all credits to Sinflower

      private long Vi( long r, string o )
      {
         for( var t = 0 ; t < o.Length ; t += 3 )
         {
            long a = o[ t + 2 ];
            a = a >= 'a' ? a - 87 : a - '0';
            a = '+' == o[ t + 1 ] ? r >> (int)a : r << (int)a;
            r = '+' == o[ t ] ? r + a & 4294967295 : r ^ a;
         }

         return r;
      }

      private string Tk( string r )
      {
         List<long> S = new List<long>();

         for( var v = 0 ; v < r.Length ; v++ )
         {
            long A = r[ v ];
            if( 128 > A )
               S.Add( A );
            else
            {
               if( 2048 > A )
                  S.Add( A >> 6 | 192 );
               else if( 55296 == ( 64512 & A ) && v + 1 < r.Length && 56320 == ( 64512 & r[ v + 1 ] ) )
               {
                  A = 65536 + ( ( 1023 & A ) << 10 ) + ( 1023 & r[ ++v ] );
                  S.Add( A >> 18 | 240 );
                  S.Add( A >> 12 & 63 | 128 );
               }
               else
               {
                  S.Add( A >> 12 | 224 );
                  S.Add( A >> 6 & 63 | 128 );
               }

               S.Add( 63 & A | 128 );
            }
         }

         const string F = "+-a^+6";
         const string D = "+-3^+b+-f";
         long p = m;

         for( var b = 0 ; b < S.Count ; b++ )
         {
            p += S[ b ];
            p = Vi( p, F );
         }

         p = Vi( p, D );
         p ^= s;
         if( 0 > p )
            p = ( 2147483647 & p ) + 2147483648;

         p %= (long)1e6;

         return p.ToString( CultureInfo.InvariantCulture ) + "." + ( p ^ m ).ToString( CultureInfo.InvariantCulture );
      }

      public override bool TryExtractTranslated( string result, out string translated )
      {
         try
         {
            var arr = JSON.Parse( result );
            var lineBuilder = new StringBuilder( result.Length );

            foreach( JSONNode entry in arr.AsArray[ 0 ].AsArray )
            {
               var token = entry.AsArray[ 0 ].ToString();
               token = token.Substring( 1, token.Length - 2 ).UnescapeJson();

               if( !lineBuilder.EndsWithWhitespaceOrNewline() ) lineBuilder.Append( "\n" );

               lineBuilder.Append( token );
            }

            translated = lineBuilder.ToString();

            var success = !string.IsNullOrEmpty( translated );
            return success;
         }
         catch
         {
            translated = null;
            return false;
         }
      }
   }
}
