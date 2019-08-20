﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
using XUnity.Common.Extensions;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;
using XUnity.ResourceRedirector.Hooks;

namespace XUnity.ResourceRedirector
{

   /// <summary>
   /// Entrypoint to the resource redirection API.
   /// </summary>
   public static class ResourceRedirection
   {
      private static readonly object Sync = new object();
      private static readonly WeakDictionary<AssetBundleRequest, AsyncAssetBundleLoadInfo> AssetBundleRequestToAssetBundle = new WeakDictionary<AssetBundleRequest, AsyncAssetBundleLoadInfo>();
      private static readonly WeakDictionary<AssetBundleRequest, bool> AssetBundleRequestToSkipPostfixes = new WeakDictionary<AssetBundleRequest, bool>();

      private static readonly List<PrioritizedItem<Action<AssetLoadedContext>>> PostfixRedirectionsForAssetsPerCall = new List<PrioritizedItem<Action<AssetLoadedContext>>>();
      private static readonly List<PrioritizedItem<Action<AssetLoadedContext>>> PostfixRedirectionsForAssetsPerResource = new List<PrioritizedItem<Action<AssetLoadedContext>>>();
      private static readonly List<PrioritizedItem<Action<ResourceLoadedContext>>> PostfixRedirectionsForResourcesPerCall = new List<PrioritizedItem<Action<ResourceLoadedContext>>>();
      private static readonly List<PrioritizedItem<Action<ResourceLoadedContext>>> PostfixRedirectionsForResourcesPerResource = new List<PrioritizedItem<Action<ResourceLoadedContext>>>();
      private static readonly List<PrioritizedItem<Action<AssetBundleLoadingContext>>> PrefixRedirectionsForAssetBundles = new List<PrioritizedItem<Action<AssetBundleLoadingContext>>>();
      private static readonly List<PrioritizedItem<Action<AsyncAssetBundleLoadingContext>>> PrefixRedirectionsForAsyncAssetBundles = new List<PrioritizedItem<Action<AsyncAssetBundleLoadingContext>>>();
      private static readonly List<PrioritizedItem<Action<AssetLoadingContext>>> PrefixRedirectionsForAssetsPerCall = new List<PrioritizedItem<Action<AssetLoadingContext>>>();
      private static readonly List<PrioritizedItem<Action<AsyncAssetLoadingContext>>> PrefixRedirectionsForAsyncAssetsPerCall = new List<PrioritizedItem<Action<AsyncAssetLoadingContext>>>();

      private static bool _initialized = false;
      private static bool _logAllLoadedResources = false;

      private static bool _isFiringAsyncAssetLoadedEvent = false;
      private static bool _isFiringAssetLoadedEvent = false;
      private static bool _isFiringResourceLoadedEvent = false;
      private static bool _isFiringAssetBundleLoadingEvent = false;
      private static bool _isFiringAsyncAssetBundleLoadingEvent = false;

      /// <summary>
      /// Gets or sets a bool indicating if the resource redirector
      /// should log all loaded resources/assets to the console.
      /// </summary>
      public static bool LogAllLoadedResources
      {
         get
         {
            return _logAllLoadedResources;
         }
         set
         {
            if( value )
            {
               Initialize();
            }

            _logAllLoadedResources = value;
         }
      }

      internal static void Initialize()
      {
         if( !_initialized )
         {
            _initialized = true;

            HookingHelper.PatchAll( ResourceAndAssetHooks.All, false );

            MaintenanceHelper.AddMaintenanceFunction( Cull, 12 );
         }
      }

      internal static void Cull()
      {
         lock( Sync )
         {
            // FIXME: Test this working as expected

            AssetBundleRequestToAssetBundle.RemoveCollectedEntries();
            AssetBundleRequestToSkipPostfixes.RemoveCollectedEntries();
         }
      }

      internal static bool ShouldSkipPostfixes( AssetBundleRequest request )
      {
         if( AssetBundleRequestToSkipPostfixes.TryGetValue( request, out var result ) )
         {
            return result;
         }
         return false;
      }

      internal static bool Hook_AssetBundleLoaded_Prefix( string path, uint crc, ulong offset, AssetBundleLoadType loadType, out AssetBundle bundle )
      {
         if( !_isFiringAssetBundleLoadingEvent )
         {
            _isFiringAssetBundleLoadingEvent = true;

            try
            {
               var context = new AssetBundleLoadingContext( path, crc, offset, loadType );
               if( _logAllLoadedResources )
               {
                  XuaLogger.ResourceRedirector.Debug( $"Loading Asset Bundle: ({context.GetNormalizedPath()})." );
               }

               var list2 = PrefixRedirectionsForAssetBundles;
               var len2 = list2.Count;
               for( int i = 0; i < len2; i++ )
               {
                  try
                  {
                     var redirection = list2[ i ];

                     redirection.Item( context );

                     if( context.SkipRemainingPrefixes ) break;
                  }
                  catch( Exception ex )
                  {
                     XuaLogger.ResourceRedirector.Error( ex, "An error occurred while invoking AssetBundleLoading event." );
                  }
               }

               bundle = context.Bundle;
               return context.SkipOriginalCall;
            }
            finally
            {
               _isFiringAssetBundleLoadingEvent = false;
            }
         }

         bundle = null;
         return false;
      }

      internal static bool Hook_AssetBundleLoading_Prefix( string path, uint crc, ulong offset, AssetBundleLoadType loadType, out AssetBundleCreateRequest request )
      {
         if( !_isFiringAsyncAssetBundleLoadingEvent )
         {
            _isFiringAsyncAssetBundleLoadingEvent = true;

            try
            {
               var context = new AsyncAssetBundleLoadingContext( path, crc, offset, loadType );
               if( _logAllLoadedResources )
               {
                  XuaLogger.ResourceRedirector.Debug( $"Loading Asset Bundle (async): ({context.GetNormalizedPath()})." );
               }

               var list2 = PrefixRedirectionsForAsyncAssetBundles;
               var len2 = list2.Count;
               for( int i = 0; i < len2; i++ )
               {
                  try
                  {
                     var redirection = list2[ i ];

                     redirection.Item( context );

                     if( context.SkipRemainingPrefixes ) break;
                  }
                  catch( Exception ex )
                  {
                     XuaLogger.ResourceRedirector.Error( ex, "An error occurred while invoking AssetBundleLoading event." );
                  }
               }

               request = context.Request;
               return context.SkipOriginalCall;
            }
            finally
            {
               _isFiringAsyncAssetBundleLoadingEvent = false;
            }
         }

         request = null;
         return false;
      }

      internal static AssetOrResourceLoadingPrefixResult Hook_AssetLoading_Prefix( string assetName, Type assetType, AssetLoadType loadType, AssetBundle parentBundle, ref UnityEngine.Object asset )
      {
         UnityEngine.Object[] arr = null;

         var intention = Hook_AssetLoading_Prefix( assetName, assetType, loadType, parentBundle, ref arr );

         if( arr == null || arr.Length == 0 )
         {
            asset = null;
         }
         else if( arr.Length > 1 )
         {
            XuaLogger.ResourceRedirector.Warn( $"Illegal behaviour by redirection handler in AssetLoaded event. Returned more than one asset to call requiring only a single asset." );
            asset = arr[ 0 ];
         }
         else if( arr.Length == 1 )
         {
            asset = arr[ 0 ];
         }

         return intention;
      }

      internal static AssetOrResourceLoadingPrefixResult Hook_AssetLoading_Prefix( string assetName, Type assetType, AssetLoadType loadType, AssetBundle bundle, ref UnityEngine.Object[] assets )
      {
         if( !_isFiringAssetLoadedEvent )
         {
            _isFiringAssetLoadedEvent = true;

            try
            {
               var context = new AssetLoadingContext( assetName, assetType, loadType, bundle );

               // handle "per call" hooks first
               var list1 = PrefixRedirectionsForAssetsPerCall;
               var len1 = list1.Count;
               for( int i = 0; i < len1; i++ )
               {
                  try
                  {
                     var redirection = list1[ i ];

                     redirection.Item( context );

                     if( context.Assets.Contains( null ) )
                     {
                        XuaLogger.ResourceRedirector.Warn( $"Illegal behaviour by redirection handler in AssetLoading event. If you want to remove an asset from the array, replace the entire array." );
                     }

                     if( context.SkipRemainingPrefixes ) break;
                  }
                  catch( Exception ex )
                  {
                     XuaLogger.ResourceRedirector.Error( ex, "An error occurred while invoking AssetLoading event." );
                  }
               }

               assets = context.Assets;

               return new AssetOrResourceLoadingPrefixResult( context.SkipOriginalCall, context.SkipAllPostfixes );
            }
            catch( Exception e )
            {
               XuaLogger.ResourceRedirector.Error( e, "An error occurred while invoking AssetLoading event." );
            }
            finally
            {
               _isFiringAssetLoadedEvent = false;
            }
         }

         return new AssetOrResourceLoadingPrefixResult( false, false );
      }

      internal static bool Hook_AsyncAssetLoading_Prefix( string assetName, Type assetType, AssetLoadType loadType, AssetBundle bundle, ref AssetBundleRequest request )
      {
         if( !_isFiringAsyncAssetLoadedEvent )
         {
            _isFiringAsyncAssetLoadedEvent = true;

            try
            {
               var context = new AsyncAssetLoadingContext( assetName, assetType, loadType, bundle );

               // handle "per call" hooks first
               var list1 = PrefixRedirectionsForAsyncAssetsPerCall;
               var len1 = list1.Count;
               for( int i = 0; i < len1; i++ )
               {
                  try
                  {
                     var redirection = list1[ i ];

                     redirection.Item( context );

                     if( context.SkipRemainingPrefixes ) break;
                  }
                  catch( Exception ex )
                  {
                     XuaLogger.ResourceRedirector.Error( ex, "An error occurred while invoking AsyncAssetLoading event." );
                  }
               }

               request = context.Request;

               AssetBundleRequestToSkipPostfixes[ request ] = context.SkipAllPostfixes;
               return context.SkipOriginalCall;
            }
            catch( Exception e )
            {
               XuaLogger.ResourceRedirector.Error( e, "An error occurred while invoking AsyncAssetLoading event." );
            }
            finally
            {
               _isFiringAsyncAssetLoadedEvent = false;
            }
         }

         return false;
      }

      internal static void Hook_AssetLoaded_Postfix( string assetName, Type assetType, AssetLoadType loadType, AssetBundle parentBundle, AssetBundleRequest request, ref UnityEngine.Object asset )
      {
         UnityEngine.Object[] arr;
         if( asset == null )
         {
            arr = new UnityEngine.Object[ 0 ];
         }
         else
         {
            arr = new[] { asset };
         }

         Hook_AssetLoaded_Postfix( assetName, assetType, loadType, parentBundle, request, ref arr );

         if( arr == null || arr.Length == 0 )
         {
            asset = null;
         }
         else if( arr.Length > 1 )
         {
            XuaLogger.ResourceRedirector.Warn( $"Illegal behaviour by redirection handler in AssetLoaded event. Returned more than one asset to call requiring only a single asset." );
            asset = arr[ 0 ];
         }
         else if( arr.Length == 1 )
         {
            asset = arr[ 0 ];
         }
      }

      internal static void Hook_AssetLoaded_Postfix( string assetName, Type assetType, AssetLoadType loadType, AssetBundle bundle, AssetBundleRequest request, ref UnityEngine.Object[] assets )
      {
         lock( Sync )
         {
            if( bundle == null )
            {
               if( !AssetBundleRequestToAssetBundle.TryGetValue( request, out var loadInfo ) )
               {
                  XuaLogger.ResourceRedirector.Error( "Could not find asset bundle from request object!" );
                  return;
               }
               else
               {
                  bundle = loadInfo.Bundle;
                  assetName = loadInfo.AssetName;
                  assetType = loadInfo.AssetType;
                  loadType = loadInfo.LoadType;
               }
            }
         }

         FireAssetLoadedEvent( assetName, assetType, bundle, loadType, ref assets );
      }

      internal static void Hook_AssetLoading_Postfix( string assetName, Type assetType, AssetLoadType loadType, AssetBundle bundle, AssetBundleRequest request )
      {
         // create ref from request to parentBundle?
         lock( Sync )
         {
            if( bundle != null && request != null )
            {
               AssetBundleRequestToAssetBundle[ request ] = new AsyncAssetBundleLoadInfo( assetName, assetType, loadType, bundle );
            }
         }
      }

      internal static void Hook_ResourceLoaded_Postfix( string assetPath, Type assetType, ResourceLoadType loadType, ref UnityEngine.Object asset )
      {
         UnityEngine.Object[] arr;
         if( asset == null )
         {
            arr = new UnityEngine.Object[ 0 ];
         }
         else
         {
            arr = new[] { asset };
         }

         Hook_ResourceLoaded_Postfix( assetPath, assetType, loadType, ref arr );

         if( arr == null || arr.Length == 0 )
         {
            asset = null;
         }
         else if( arr.Length > 1 )
         {
            XuaLogger.ResourceRedirector.Warn( $"Illegal behaviour by redirection handler in ResourceLoaded event. Returned more than one asset to call requiring only a single asset." );
            asset = arr[ 0 ];
         }
         else if( arr.Length == 1 )
         {
            asset = arr[ 0 ];
         }
      }

      internal static void Hook_ResourceLoaded_Postfix( string assetPath, Type assetType, ResourceLoadType loadType, ref UnityEngine.Object[] assets )
      {
         FireResourceLoadedEvent( assetPath, assetType, loadType, ref assets );
      }

      internal static void FireAssetLoadedEvent( string assetLoadName, Type assetLoadType, AssetBundle assetBundle, AssetLoadType loadType, ref UnityEngine.Object[] assets )
      {
         if( !_isFiringAssetLoadedEvent )
         {
            _isFiringAssetLoadedEvent = true;

            var originalAssets = assets?.ToArray();
            try
            {
               var contextPerCall = new AssetLoadedContext( assetLoadName, assetLoadType, loadType, assetBundle, assets );

               if( _logAllLoadedResources && assets != null )
               {
                  for( int i = 0; i < assets.Length; i++ )
                  {
                     var asset = assets[ i ];
                     var uniquePath = contextPerCall.GetUniqueFileSystemAssetPath( asset );
                     XuaLogger.ResourceRedirector.Debug( $"Loaded Asset: '{asset.GetType().FullName}', Load Type: '{loadType.ToString()}', Unique Path: ({uniquePath})." );
                  }
               }

               // handle "per call" hooks first
               var list1 = PostfixRedirectionsForAssetsPerCall;
               var len1 = list1.Count;
               for( int i = 0; i < len1; i++ )
               {
                  try
                  {
                     var redirection = list1[ i ];

                     redirection.Item( contextPerCall );

                     if( contextPerCall.Assets.Contains( null ) )
                     {
                        XuaLogger.ResourceRedirector.Warn( $"Illegal behaviour by redirection handler in AssetLoaded event. If you want to remove an asset from the array, replace the entire array." );
                     }

                     if( contextPerCall.SkipRemainingPostfixes ) break;
                  }
                  catch( Exception ex )
                  {
                     XuaLogger.ResourceRedirector.Error( ex, "An error occurred while invoking AssetLoaded event." );
                  }
               }

               assets = contextPerCall.Assets;

               // handle "per resource" hooks afterwards
               if( !contextPerCall.SkipRemainingPostfixes && assets != null )
               {
                  for( int j = 0; j < assets.Length; j++ )
                  {
                     var asset = new[] { assets[ j ] };
                     if( asset != null )
                     {
                        var contextPerResource = new AssetLoadedContext( assetLoadName, assetLoadType, loadType, assetBundle, asset );

                        var list2 = PostfixRedirectionsForAssetsPerResource;
                        var len2 = list2.Count;
                        for( int i = 0; i < len2; i++ )
                        {
                           try
                           {
                              var redirection = list2[ i ];

                              redirection.Item( contextPerResource );

                              if( contextPerResource.Assets != null && contextPerResource.Assets.Length == 1 && contextPerResource.Assets[ 0 ] != null )
                              {
                                 assets[ j ] = contextPerResource.Assets[ 0 ];
                              }
                              else
                              {
                                 XuaLogger.ResourceRedirector.Warn( $"Illegal behaviour by redirection handler in AssetLoaded event. You must not remove an asset reference when hooking with behaviour {HookBehaviour.OneCallbackPerResourceLoaded}." );
                              }

                              if( contextPerResource.SkipRemainingPostfixes ) break;
                           }
                           catch( Exception ex )
                           {
                              XuaLogger.ResourceRedirector.Error( ex, "An error occurred while invoking AssetLoaded event." );
                           }
                        }
                     }
                     else
                     {
                        XuaLogger.ResourceRedirector.Error( "Found unexpected null asset during AssetLoaded event." );
                     }
                  }
               }
            }
            catch( Exception e )
            {
               XuaLogger.ResourceRedirector.Error( e, "An error occurred while invoking AssetLoaded event." );
            }
            finally
            {
               if( originalAssets != null )
               {
                  foreach( var asset in originalAssets )
                  {
                     var ext = asset.GetOrCreateExtensionData<ResourceExtensionData>();
                     ext.HasBeenRedirected = true;
                  }
               }

               _isFiringAssetLoadedEvent = false;
            }
         }
      }

      internal static void FireResourceLoadedEvent( string resourceLoadPath, Type resourceLoadType, ResourceLoadType loadType, ref UnityEngine.Object[] assets )
      {
         if( !_isFiringResourceLoadedEvent )
         {
            _isFiringResourceLoadedEvent = true;

            var originalAssets = assets?.ToArray();
            try
            {
               var contextPerCall = new ResourceLoadedContext( resourceLoadPath, resourceLoadType, loadType, assets );

               if( _logAllLoadedResources && assets != null )
               {
                  for( int i = 0; i < assets.Length; i++ )
                  {
                     var asset = assets[ i ];
                     var uniquePath = contextPerCall.GetUniqueFileSystemAssetPath( asset );
                     XuaLogger.ResourceRedirector.Debug( $"Loaded Resource: '{asset.GetType().FullName}', Load Type: '{loadType.ToString()}', Unique Path: ({uniquePath})." );
                  }
               }

               // handle "per call" hooks first
               var list1 = PostfixRedirectionsForResourcesPerCall;
               var len1 = list1.Count;
               for( int i = 0; i < len1; i++ )
               {
                  try
                  {
                     var redirection = list1[ i ];

                     redirection.Item( contextPerCall );

                     if( contextPerCall.Assets.Contains( null ) )
                     {
                        XuaLogger.ResourceRedirector.Warn( $"Illegal behaviour by redirection handler in ResourceLoaded event. If you want to remove an asset from the array, replace the entire array." );
                     }

                     if( contextPerCall.SkipRemainingPostfixes ) break;
                  }
                  catch( Exception ex )
                  {
                     XuaLogger.ResourceRedirector.Error( ex, "An error occurred while invoking ResourceLoaded event." );
                  }
               }

               assets = contextPerCall.Assets;

               // handle "per resource" hooks afterwards
               if( !contextPerCall.SkipRemainingPostfixes && assets != null )
               {
                  for( int j = 0; j < assets.Length; j++ )
                  {
                     var asset = new[] { assets[ j ] };
                     if( asset != null )
                     {
                        var contextPerResource = new ResourceLoadedContext( resourceLoadPath, resourceLoadType, loadType, asset );

                        var list2 = PostfixRedirectionsForResourcesPerResource;
                        var len2 = list2.Count;
                        for( int i = 0; i < len2; i++ )
                        {
                           try
                           {
                              var redirection = list2[ i ];

                              redirection.Item( contextPerResource );

                              if( contextPerResource.Assets != null && contextPerResource.Assets.Length == 1 && contextPerResource.Assets[ 0 ] != null )
                              {
                                 assets[ j ] = contextPerResource.Assets[ 0 ];
                              }
                              else
                              {
                                 XuaLogger.ResourceRedirector.Warn( $"Illegal behaviour by redirection handler in ResourceLoaded event. You must not remove an asset reference when hooking with behaviour {HookBehaviour.OneCallbackPerResourceLoaded}." );
                              }

                              if( contextPerResource.SkipRemainingPostfixes ) break;
                           }
                           catch( Exception ex )
                           {
                              XuaLogger.ResourceRedirector.Error( ex, "An error occurred while invoking ResourceLoaded event." );
                           }
                        }
                     }
                     else
                     {
                        XuaLogger.ResourceRedirector.Error( "Found unexpected null asset during ResourceLoaded event." );
                     }
                  }
               }
            }
            catch( Exception e )
            {
               XuaLogger.ResourceRedirector.Error( e, "An error occurred while invoking ResourceLoaded event." );
            }
            finally
            {
               if( originalAssets != null )
               {
                  foreach( var asset in originalAssets )
                  {
                     var ext = asset.GetOrCreateExtensionData<ResourceExtensionData>();
                     ext.HasBeenRedirected = true;
                  }
               }

               _isFiringResourceLoadedEvent = false;
            }
         }
      }

      /// <summary>
      /// Register an AssetLoading hook (prefix to loading an asset from an asset bundle).
      /// </summary>
      /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
      /// <param name="action">The callback.</param>
      public static void RegisterAssetLoadingHook( int priority, Action<AssetLoadingContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         var item = PrioritizedItem.Create( action, priority );
         if( PrefixRedirectionsForAssetsPerCall.Contains( item ) )
         {
            throw new ArgumentException( "This callback has already been registered.", "action" );
         }

         Initialize();

         PrefixRedirectionsForAssetsPerCall.Add( item );
      }

      /// <summary>
      /// Unregister an AssetLoading hook (prefix to loading an asset from an asset bundle).
      /// </summary>
      /// <param name="action">The callback.</param>
      public static void UnregisterAssetLoadingHook( Action<AssetLoadingContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         PrefixRedirectionsForAssetsPerCall.RemoveAll( x => x.Item == action );
      }

      /// <summary>
      /// Register an AsyncAssetLoading hook (prefix to loading an asset from an asset bundle asynchronously).
      /// </summary>
      /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
      /// <param name="action">The callback.</param>
      public static void RegisterAsyncAssetLoadingHook( int priority, Action<AsyncAssetLoadingContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         var item = PrioritizedItem.Create( action, priority );
         if( PrefixRedirectionsForAsyncAssetsPerCall.Contains( item ) )
         {
            throw new ArgumentException( "This callback has already been registered.", "action" );
         }

         Initialize();

         PrefixRedirectionsForAsyncAssetsPerCall.Add( item );
      }

      /// <summary>
      /// Unregister an AsyncAssetLoading hook (prefix to loading an asset from an asset bundle asynchronously).
      /// </summary>
      /// <param name="action">The callback.</param>
      public static void UnregisterAsyncAssetLoadingHook( Action<AsyncAssetLoadingContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         PrefixRedirectionsForAsyncAssetsPerCall.RemoveAll( x => x.Item == action );
      }

      /// <summary>
      /// Register an AssetLoaded hook (postfix to loading an asset from an asset bundle (both synchronous and asynchronous)).
      /// </summary>
      /// <param name="behaviour">The behaviour of the callback.</param>
      /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
      /// <param name="action">The callback.</param>
      public static void RegisterAssetLoadedHook( HookBehaviour behaviour, int priority, Action<AssetLoadedContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         var item = PrioritizedItem.Create( action, priority );
         if( PostfixRedirectionsForAssetsPerCall.Contains( item )
            || PostfixRedirectionsForAssetsPerResource.Contains( item ) )
         {
            throw new ArgumentException( "This callback has already been registered.", "action" );
         }

         Initialize();

         if( behaviour == HookBehaviour.OneCallbackPerLoadCall )
         {
            PostfixRedirectionsForAssetsPerCall.Add( item );
         }
         else if( behaviour == HookBehaviour.OneCallbackPerResourceLoaded )
         {
            PostfixRedirectionsForAssetsPerResource.Add( item );
         }
      }

      /// <summary>
      /// Unregister an AssetLoaded hook (postfix to loading an asset from an asset bundle (both synchronous and asynchronous)).
      /// </summary>
      /// <param name="action">The callback.</param>
      public static void UnregisterAssetLoadedHook( Action<AssetLoadedContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         PostfixRedirectionsForAssetsPerCall.RemoveAll( x => x.Item == action );
         PostfixRedirectionsForAssetsPerResource.RemoveAll( x => x.Item == action );
      }

      /// <summary>
      /// Register an AssetBundleLoading hook (prefix to loading an asset bundle synchronously).
      /// </summary>
      /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
      /// <param name="action">The callback.</param>
      public static void RegisterAssetBundleLoadingHook( int priority, Action<AssetBundleLoadingContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         var item = PrioritizedItem.Create( action, priority );
         if( PrefixRedirectionsForAssetBundles.Contains( item ) )
         {
            throw new ArgumentException( "This callback has already been registered.", "action" );
         }

         Initialize();

         PrefixRedirectionsForAssetBundles.Add( item );
      }

      /// <summary>
      /// Unregister an AssetBundleLoading hook (prefix to loading an asset bundle synchronously).
      /// </summary>
      /// <param name="action">The callback.</param>
      public static void UnregisterAssetBundleLoadingHook( Action<AssetBundleLoadingContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         PrefixRedirectionsForAssetBundles.RemoveAll( x => x.Item == action );
      }

      /// <summary>
      /// Register an AsyncAssetBundleLoading hook (prefix to loading an asset bundle asynchronously).
      /// </summary>
      /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
      /// <param name="action">The callback.</param>
      public static void RegisterAsyncAssetBundleLoadingHook( int priority, Action<AsyncAssetBundleLoadingContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         var item = PrioritizedItem.Create( action, priority );
         if( PrefixRedirectionsForAsyncAssetBundles.Contains( item ) )
         {
            throw new ArgumentException( "This callback has already been registered.", "action" );
         }

         Initialize();

         PrefixRedirectionsForAsyncAssetBundles.Add( item );
      }

      /// <summary>
      /// Unregister an AsyncAssetBundleLoading hook (prefix to loading an asset bundle asynchronously).
      /// </summary>
      /// <param name="action">The callback.</param>
      public static void UnregisterAsyncAssetBundleLoadingHook( Action<AsyncAssetBundleLoadingContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         PrefixRedirectionsForAsyncAssetBundles.RemoveAll( x => x.Item == action );
      }

      /// <summary>
      /// Register a ReourceLoaded hook (postfix to loading a resource from the Resources API (both synchronous and asynchronous)).
      /// </summary>
      /// <param name="behaviour">The behaviour of the callback.</param>
      /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
      /// <param name="action">The callback.</param>
      public static void RegisterResourceLoadedHook( HookBehaviour behaviour, int priority, Action<ResourceLoadedContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         var item = PrioritizedItem.Create( action, priority );
         if( PostfixRedirectionsForResourcesPerCall.Contains( item )
            || PostfixRedirectionsForResourcesPerResource.Contains( item ) )
         {
            throw new ArgumentException( "This callback has already been registered.", "action" );
         }

         Initialize();

         if( behaviour == HookBehaviour.OneCallbackPerLoadCall )
         {
            PostfixRedirectionsForResourcesPerCall.BinarySearchInsert( item );
         }
         else if( behaviour == HookBehaviour.OneCallbackPerResourceLoaded )
         {
            PostfixRedirectionsForResourcesPerResource.BinarySearchInsert( item );
         }
      }

      /// <summary>
      /// Unregister a ReourceLoaded hook (postfix to loading a resource from the Resources API (both synchronous and asynchronous)).
      /// </summary>
      /// <param name="action">The callback.</param>
      public static void UnregisterResourceLoadedHook( Action<ResourceLoadedContext> action )
      {
         if( action == null ) throw new ArgumentNullException( "action" );

         PostfixRedirectionsForResourcesPerCall.RemoveAll( x => x.Item == action );
         PostfixRedirectionsForResourcesPerResource.RemoveAll( x => x.Item == action );
      }
   }
}
