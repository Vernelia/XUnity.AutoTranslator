﻿using System;
using System.IO;
using System.Text;
using System.Threading;

namespace XUnity.AutoTranslator.Plugin.Lec
{
   class Program
   {
      static void Main( string[] args )
      {
         try
         {
            if( args.Length == 0 )
            {
               Console.WriteLine( "This program is automatically run during a game session if LEC is selected, and will be automatically stopped afterwards. This program cannot be run independently." );
               Console.WriteLine( "Press any key to exit." );
               Console.ReadKey();
               return;
            }

            var powerTranslatorPathPayload = args[ 0 ];
            var powerTranslatorPath = Encoding.UTF8.GetString( Convert.FromBase64String( powerTranslatorPathPayload ) );
            var dllPath = Path.Combine( powerTranslatorPath, @"Nova\JaEn\EngineDll_je.dll" );

            using( var translator = new LecTranslationLibrary( dllPath ) )
            {
               while( true )
               {
                  using( var stdout = Console.OpenStandardOutput() )
                  using( var writer = new StreamWriter( stdout ) )
                  using( var stdin = Console.OpenStandardInput() )
                  using( var reader = new StreamReader( stdin ) )
                  {
                     var receivedPayload = reader.ReadLine();
                     if( string.IsNullOrEmpty( receivedPayload ) )
                     {
                        return;
                     }

                     var receivedLine = Encoding.UTF8.GetString( Convert.FromBase64String( receivedPayload ) );
                     var translatedLine = translator.Translate( receivedLine );

                     var translatedPayload = Convert.ToBase64String( Encoding.UTF8.GetBytes( translatedLine ) );
                     writer.WriteLine( translatedPayload );
                  }
               }
            }
         }
         catch( Exception )
         {
            // "Graceful shutdown"
         }
      }
   }
}
