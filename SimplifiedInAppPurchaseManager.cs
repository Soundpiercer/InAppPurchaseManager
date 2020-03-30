// Author : Soundpiercer 2019-11-15
// soundpiercer@gmail.com
//
// Simplified InAppPurchaseManager
// If you want to use advance features, see InAppPurchaseManager.cs
//
// [How to Use]
// 1. Attach this script into your GameObject
// 2. Call BuyProductID from outside to Start Purchase

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
using UnityEngine.Purchasing.Security;

/// <summary>
/// Unity IAP Manager with basic features
/// </summary>
public class SimplifiedInAppPurchaseManager : MonoBehaviour, IStoreListener
{
    #region Singleton (Simplified)
    public static SimplifiedInAppPurchaseManager Instance;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else if (Instance != this)
            Destroy(gameObject);

        SetToDontDestroyOnLoad();
    }

    private void SetToDontDestroyOnLoad()
    {
        DontDestroyOnLoad(gameObject);
    }
    #endregion

    // Unity IAP
    private static IStoreController storeController;          // The Unity Purchasing system.
    private static IExtensionProvider storeExtensionProvider; // The store-specific Purchasing subsystems.

    // Managed Products.
    public List<string> managedProducts = new List<string>();

    /// <summary>
    /// is your purchase in Client Side Steps? (Client - IAP - App Store - IAP - Client)
    /// </summary>
    [HideInInspector]
    public bool isBuyingInClient;

    /// <summary>
    /// is your purchase in Full Process Steps? (Client - IAP - App Store - IAP - Client - Server - Client)
    /// </summary>
    [HideInInspector]
    public bool isPurchaseUnderProcess;

    private IEnumerator Start()
    {
        // Should define managed products list to be used.
        InitializeManagedProducts();

        // Unity IAP
        if (storeController == null)
        {
            InitializePurchasing();
        }

        yield break;
    }

    #region Start Subroutines
    private void InitializeManagedProducts()
    {
        // ################# Initialize your product ID List
        //managedProducts = new List<string>{"testProduct"};
    }

    private bool IsInitialized()
    {
        return storeController != null && storeExtensionProvider != null;
    }

    private void InitializePurchasing()
    {
        if (IsInitialized())
            return;

        var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());

#if UNITY_ANDROID
        string storeName = GooglePlay.Name;
#elif UNITY_IOS
        string storeName = AppleAppStore.Name;
#endif

        // Add managedProducts to the builder.
        foreach (string productName in managedProducts)
        {
            builder.AddProduct(
                productName,
                ProductType.Consumable,
                new IDs {{ productName, storeName }
                });
        }

        UnityPurchasing.Initialize(this, builder);
    }

    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        Debug.Log("[In-App Purchase Manager] >>>>>> OnInitialized: PASS");

        storeController = controller;
        storeExtensionProvider = extensions;
    }

    public void OnInitializeFailed(InitializationFailureReason error)
    {
        Debug.LogError("[In-App Purchase Manager] >>>>>> OnInitializeFailed InitializationFailureReason:" + error);
    }
    #endregion

    #region Unity IAP (Transaction Control, Client Side Purchase Restoration)
    public IEnumerator BuyProductID(string productId)
    {
        if (IsInitialized())
        {
            Product product = storeController.products.WithID(productId);

            if (product != null && product.availableToPurchase)
            {
                Debug.Log(string.Format("[In-App Purchase Manager] <<<<<< Purchasing product asychronously: '{0}'", product.definition.id));
                storeController.InitiatePurchase(product);
            }
            else
            {
                Debug.LogError("[In-App Purchase Manager] >>>>>> BuyProductID: FAIL. Not purchasing product, either is not found or is not available for purchase");
            }
        }
        else
        {
            Debug.LogError("[In-App Purchase Manager] >>>>>> BuyProductID FAIL. Not initialized.");
        }

        yield return null;
    }

    // Automatically Called by Unity IAP when
    // 1. NORMAL PURCHASE (Charging is done, waiting for transaction)
    // 2. PURCHASE RESTORATION (Restoring Purchase on Init)
    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
    {
        // Client Side Receipt Validation
        bool validPurchase = CheckReceipt(args.purchasedProduct);

        if (validPurchase)
        {
            Debug.Log(string.Format("[In-App Purchase Manager] >>>>>> ProcessPurchase: PASS. Product: '{0}'", args.purchasedProduct.definition.id));
        }
        else
        {
            Debug.LogError(string.Format("[In-App Purchase Manager] >>>>>> ProcessPurchase: Fail (receipt Valid Error)  Product: '{0}'", args.purchasedProduct.definition.id));
        }

        isBuyingInClient = false;

        // NORMAL PURCHASE
        Debug.LogWarning("[In-App Purchase Manager] >>>>>> Simplified Purchase Complete!");
        return PurchaseProcessingResult.Complete;
    }

    private bool CheckReceipt(Product product)
    {
        var validator = new CrossPlatformValidator(GooglePlayTangle.Data(), AppleTangle.Data(), Application.identifier);
        try
        {
            Debug.Log(product.receipt);
            var result = validator.Validate(product.receipt);
        }
        catch (IAPSecurityException)
        {
            Debug.LogError("Invalid Receipt");
            return false;
        }

        return true;
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
    {
        Debug.LogErrorFormat(string.Format("OnPurchaseFailed: FAIL. Product: '{0}', PurchaseFailureReason: {1}", product.definition.storeSpecificId, failureReason));
        isBuyingInClient = false;
    }

    /// <summary>
    /// Extract Payload part from your receipt for Server Side Receipt Validation.
    /// </summary>
    public static string ExtractPayload(string receipt)
    {
        string[] separators = { "\"Payload\":\"" };
        string[] splittedStrings = receipt.Split(separators, StringSplitOptions.None);

        receipt = splittedStrings[1].Replace("\"}", "");
        return receipt;
    }
    #endregion
}