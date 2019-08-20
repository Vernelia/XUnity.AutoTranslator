﻿namespace XUnity.ResourceRedirector
{
   /// <summary>
   /// Shared interface between AssetLoadedContext and ResourceLoadedContext.
   /// </summary>
   public interface IAssetOrResourceLoadedContext
   {
      /// <summary>
      /// Gets a bool indicating if this resource has already been redirected before.
      /// </summary>
      bool HasReferenceBeenRedirectedBefore( UnityEngine.Object asset );

      /// <summary>
      /// Gets a file system path for the specfic asset that should be unique.
      /// </summary>
      /// <param name="asset"></param>
      /// <returns></returns>
      string GetUniqueFileSystemAssetPath( UnityEngine.Object asset );

      /// <summary>
      /// Gets the loaded assets. Override individual indices to change the asset reference that will be loaded.
      /// </summary>
      UnityEngine.Object[] Assets { get; set; }

      /// <summary>
      /// Gets the loaded asset. This is simply equal to the first index of the Assets property, with some
      /// additional null guards to prevent NullReferenceExceptions when using it.
      /// </summary>
      UnityEngine.Object Asset { get; set; }

      /// <summary>
      /// Indicate your work is done and if any other hooks to this asset/resource load should be called.
      /// </summary>
      /// <param name="skipRemainingPostfixes">Indicate if any other hooks should be skipped.</param>
      void Complete( bool skipRemainingPostfixes = true );
   }
}
