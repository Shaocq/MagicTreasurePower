// 来源：ModSetting 官方提供的反射封装（ModSettingAPI.cs），按用户 2026-10-28 的要求原样引入。
// 方案 1（松耦合）：本 mod 不引用 ModSetting.dll，只靠反射调用 ModSetting.ModBehaviour 的静态方法；
// ModSetting 未启用 / 加载顺序不同都不影响本 mod 运行，只是设置页不出现本 mod 的条目。
// 若以后需要 ModSetting 的存档系统或极致性能，再改成方案 2（直接引用 ModSetting.dll + SettingsBuilder）。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Duckov.Modding;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicTreasurePower.ModSettingIntegration;

internal static class ModSettingAPI
{
	private const string ADD_DROP_DOWN_LIST = "AddDropDownList";

	private const string ADD_SLIDER = "AddSlider";

	private const string ADD_TOGGLE = "AddToggle";

	private const string GET_VALUE = "GetValue";

	private const string SET_VALUE = "SetValue";

	private const string REMOVE_UI = "RemoveUI";

	private const string REMOVE_MOD = "RemoveMod";

	private const string ADD_INPUT = "AddInput";

	private const string HAS_CONFIG = "HasConfig";

	private const string GET_SAVED_VALUE = "GetSavedValue";

	private const string ADD_KEYBINDING_WITH_DEFAULT = "AddKeybindingWithDefault";

	private const string ADD_BUTTON = "AddButton";

	private const string ADD_GROUP = "AddGroup";

	private const string ADD_KEYBINDING_WITH_KEY = "AddKeybindingWithKey";

	private const string CLEAR = "Clear";

	private static readonly Version VERSION = new Version(0, 5, 0);

	public const string MOD_NAME = "ModSetting";

	private const string TYPE_NAME = "ModSetting.ModBehaviour";

	private static Type modBehaviour;

	private static ModInfo modInfo;

	private static Dictionary<string, Delegate> methodCache = new Dictionary<string, Delegate>();

	private static readonly string[] methodNames = new string[15]
	{
		"AddDropDownList", "AddSlider", "AddToggle", "GetValue", "SetValue", "RemoveUI", "RemoveMod", "AddInput", "HasConfig", "GetSavedValue",
		"AddKeybindingWithDefault", "AddButton", "AddGroup", "AddKeybindingWithKey", "Clear"
	};

	public static bool IsInit { get; private set; }

	/// <summary>ModSetting 程序集是否已经加载（不打印任何日志，供重试循环静默探测）。</summary>
	public static bool IsAssemblyLoaded()
	{
		return FindTypeSilently("ModSetting.ModBehaviour") != null;
	}

	private static Type FindTypeSilently(string typeName)
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		for (int i = 0; i < assemblies.Length; i++)
		{
			try
			{
				Type type = assemblies[i].GetType(typeName);
				if (type != null)
					return type;
			}
			catch
			{
			}
		}

		return null;
	}

	/// <summary>
	/// ModSetting 内部是否已经完成初始化（已读过它自己的存档）。
	/// Add* 调用会被它排队，但 GetSavedValue 不会——必须等它 isInit 才能读到上次保存的值。
	/// </summary>
	public static bool IsReady
	{
		get
		{
			if (!IsInit)
				return false;

			try
			{
				FieldInfo flagInfo = modBehaviour.GetField(
					"isInit", BindingFlags.Static | BindingFlags.NonPublic);

				// 字段名在未来版本里改了也不要卡死：找不到就当作已就绪
				if (flagInfo == null)
					return true;

				return flagInfo.GetValue(null) is bool ready && ready;
			}
			catch
			{
				return true;
			}
		}
	}

	public static bool Init(ModInfo modInfo)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		if (IsInit)
		{
			return true;
		}
		if (modInfo.name == "ModSetting")
		{
			Debug.LogError((object)"初始化失败，不能使用ModSetting的info进行初始化");
			return false;
		}
		ModSettingAPI.modInfo = modInfo;
		modBehaviour = FindTypeInAssemblies("ModSetting.ModBehaviour");
		if (modBehaviour == null)
		{
			return false;
		}
		VersionAvailable();
		string[] array = methodNames;
		foreach (string methodName in array)
		{
			MethodInfo[] array2 = (from m in modBehaviour.GetMethods(BindingFlags.Static | BindingFlags.Public)
				where m.Name == methodName
				select m).ToArray();
			if (array2.Length == 0)
			{
				Debug.LogError((object)(methodName + "方法找不到"));
				return false;
			}
		}
		IsInit = true;
		return true;
	}

	public static bool AddDropdownList(string key, string description, List<string> options, string defaultValue, Action<string> onValueChange = null)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		Type typeFromHandle = typeof(Action<ModInfo, string, string, List<string>, string, Action<string>>);
		return InvokeMethod("AddDropDownList", "AddDropDownList", new object[6] { modInfo, key, description, options, defaultValue, onValueChange }, typeFromHandle, new Type[6]
		{
			typeof(ModInfo),
			typeof(string),
			typeof(string),
			typeof(List<string>),
			typeof(string),
			typeof(Action<string>)
		});
	}

	public static bool AddSlider(string key, string description, float defaultValue, Vector2 sliderRange, Action<float> onValueChange = null, int decimalPlaces = 1, int characterLimit = 5)
	{
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		Type[] paramTypes = new Type[8]
		{
			typeof(ModInfo),
			typeof(string),
			typeof(string),
			typeof(float),
			typeof(Vector2),
			typeof(Action<float>),
			typeof(int),
			typeof(int)
		};
		Type typeFromHandle = typeof(Action<ModInfo, string, string, float, Vector2, Action<float>, int, int>);
		return InvokeMethod("AddSliderFloat", "AddSlider", new object[8] { modInfo, key, description, defaultValue, sliderRange, onValueChange, decimalPlaces, characterLimit }, typeFromHandle, paramTypes);
	}

	public static bool AddSlider(string key, string description, int defaultValue, int minValue, int maxValue, Action<int> onValueChange = null, int characterLimit = 5)
	{
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		Type[] paramTypes = new Type[8]
		{
			typeof(ModInfo),
			typeof(string),
			typeof(string),
			typeof(int),
			typeof(int),
			typeof(int),
			typeof(Action<int>),
			typeof(int)
		};
		Type typeFromHandle = typeof(Action<ModInfo, string, string, int, int, int, Action<int>, int>);
		return InvokeMethod("AddSliderInt", "AddSlider", new object[8] { modInfo, key, description, defaultValue, minValue, maxValue, onValueChange, characterLimit }, typeFromHandle, paramTypes);
	}

	public static bool AddToggle(string key, string description, bool enable, Action<bool> onValueChange = null)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		return InvokeMethod("AddToggle", "AddToggle", new object[5] { modInfo, key, description, enable, onValueChange }, typeof(Action<ModInfo, string, string, bool, Action<bool>>), new Type[5]
		{
			typeof(ModInfo),
			typeof(string),
			typeof(string),
			typeof(bool),
			typeof(Action<bool>)
		});
	}

	public static bool AddKeybinding(string key, string description, KeyCode keyCode, KeyCode defaultKeyCode = (KeyCode)0, Action<KeyCode> onValueChange = null)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		return InvokeMethod("AddKeybindingWithDefault", "AddKeybindingWithDefault", new object[6] { modInfo, key, description, keyCode, defaultKeyCode, onValueChange }, typeof(Action<ModInfo, string, string, KeyCode, KeyCode, Action<KeyCode>>), new Type[6]
		{
			typeof(ModInfo),
			typeof(string),
			typeof(string),
			typeof(KeyCode),
			typeof(KeyCode),
			typeof(Action<KeyCode>)
		});
	}

	public static bool AddKeybinding(string key, string description, Key currentKey, Key defaultKey, Action<Key> onValueChange = null)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		return InvokeMethod("AddKeybindingWithKey", "AddKeybindingWithKey", new object[6] { modInfo, key, description, currentKey, defaultKey, onValueChange }, typeof(Action<ModInfo, string, string, Key, Key, Action<Key>>), new Type[6]
		{
			typeof(ModInfo),
			typeof(string),
			typeof(string),
			typeof(Key),
			typeof(Key),
			typeof(Action<Key>)
		});
	}

	public static bool AddInput(string key, string description, string defaultValue, int characterLimit = 40, Action<string> onValueChange = null)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		return InvokeMethod("AddInput", "AddInput", new object[6] { modInfo, key, description, defaultValue, characterLimit, onValueChange }, typeof(Action<ModInfo, string, string, string, int, Action<string>>), new Type[6]
		{
			typeof(ModInfo),
			typeof(string),
			typeof(string),
			typeof(string),
			typeof(int),
			typeof(Action<string>)
		});
	}

	public static bool AddButton(string key, string description, string buttonText = "按钮", Action onClickButton = null)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		return InvokeMethod("AddButton", "AddButton", new object[5] { modInfo, key, description, buttonText, onClickButton }, typeof(Action<ModInfo, string, string, string, Action>), new Type[5]
		{
			typeof(ModInfo),
			typeof(string),
			typeof(string),
			typeof(string),
			typeof(Action)
		});
	}

	public static bool AddGroup(string key, string description, List<string> keys, float scale = 0.7f, bool topInsert = false, bool open = false)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		return InvokeMethod("AddGroup", "AddGroup", new object[7] { modInfo, key, description, keys, scale, topInsert, open }, typeof(Action<ModInfo, string, string, List<string>, float, bool, bool>), new Type[7]
		{
			typeof(ModInfo),
			typeof(string),
			typeof(string),
			typeof(List<string>),
			typeof(float),
			typeof(bool),
			typeof(bool)
		});
	}

	public static bool GetValue<T>(string key, Action<T> callback = null)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		MethodInfo staticPublicMethodInfo = GetStaticPublicMethodInfo("GetValue");
		if (staticPublicMethodInfo == null)
		{
			return false;
		}
		MethodInfo methodInfo = staticPublicMethodInfo.MakeGenericMethod(typeof(T));
		methodInfo.Invoke(null, new object[3] { modInfo, key, callback });
		return true;
	}

	public static bool SetValue<T>(string key, T value, Action<bool> callback = null)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		MethodInfo staticPublicMethodInfo = GetStaticPublicMethodInfo("SetValue");
		if (staticPublicMethodInfo == null)
		{
			return false;
		}
		MethodInfo methodInfo = staticPublicMethodInfo.MakeGenericMethod(typeof(T));
		methodInfo.Invoke(null, new object[4] { modInfo, key, value, callback });
		return true;
	}

	public static bool HasConfig()
	{
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		if (!Available())
		{
			return false;
		}
		MethodInfo staticPublicMethodInfo = GetStaticPublicMethodInfo("HasConfig");
		if (staticPublicMethodInfo == null)
		{
			return false;
		}
		return (bool)staticPublicMethodInfo.Invoke(null, new object[1] { modInfo });
	}

	public static bool GetSavedValue<T>(string key, out T value)
	{
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		value = default;
		if (!Available(key))
		{
			return false;
		}
		MethodInfo staticPublicMethodInfo = GetStaticPublicMethodInfo("GetSavedValue");
		if (staticPublicMethodInfo == null)
		{
			return false;
		}
		MethodInfo methodInfo = staticPublicMethodInfo.MakeGenericMethod(typeof(T));
		object[] array = new object[3] { modInfo, key, null };
		bool result = (bool)methodInfo.Invoke(null, array);
		value = (T)array[2];
		return result;
	}

	public static bool RemoveUI(string key, Action<bool> callback = null)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		if (!Available(key))
		{
			return false;
		}
		return InvokeMethod("RemoveUI", "RemoveUI", new object[3] { modInfo, key, callback }, typeof(Action<ModInfo, string, Action<bool>>), new Type[3]
		{
			typeof(ModInfo),
			typeof(string),
			typeof(Action<bool>)
		});
	}

	public static bool RemoveMod(Action<bool> callback = null)
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		if (!Available())
		{
			return false;
		}
		return InvokeMethod("RemoveMod", "RemoveMod", new object[2] { modInfo, callback }, typeof(Action<ModInfo, Action<bool>>), new Type[2]
		{
			typeof(ModInfo),
			typeof(Action<bool>)
		});
	}

	public static bool Clear(Action<bool> callback = null)
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		if (!Available())
		{
			return false;
		}
		return InvokeMethod("Clear", "Clear", new object[2] { modInfo, callback }, typeof(Action<ModInfo, Action<bool>>), new Type[2]
		{
			typeof(ModInfo),
			typeof(Action<bool>)
		});
	}

	private static bool Available()
	{
		return IsInit && modInfo.displayName != null && modInfo.name != null;
	}

	private static bool Available(string key)
	{
		return IsInit && modInfo.displayName != null && modInfo.name != null && key != null;
	}

	private static bool VersionAvailable()
	{
		FieldInfo field = modBehaviour.GetField("VERSION", BindingFlags.Static | BindingFlags.Public);
		if (field != null && field.FieldType == typeof(float))
		{
			Version version = (Version)field.GetValue(null);
			if (version != VERSION)
			{
				Debug.LogWarning((object)$"警告:ModSetting的版本:{version} (API的版本:{VERSION}),新功能将无法使用");
				return false;
			}
			return true;
		}
		return false;
	}

	private static Type FindTypeInAssemblies(string typeName)
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		Assembly[] array = assemblies;
		foreach (Assembly assembly in array)
		{
			Type type = assembly.GetType(typeName);
			if (type != null)
			{
				return type;
			}
		}
		Debug.LogWarning((object)("(Mod:" + modInfo.displayName + ")找不到ModSetting程序集"));
		return null;
	}

	private static MethodInfo GetStaticPublicMethodInfo(string methodName, Type[] parameterTypes = null)
	{
		if (!IsInit)
		{
			return null;
		}
		BindingFlags bindingAttr = BindingFlags.Static | BindingFlags.Public;
		if (parameterTypes != null)
		{
			MethodInfo[] source = (from m in modBehaviour.GetMethods(bindingAttr)
				where m.Name == methodName
				select m).ToArray();
			return source.Where((MethodInfo methodInfo) =>
			{
				ParameterInfo[] parameters = methodInfo.GetParameters();
				if (parameters.Length != parameterTypes.Length)
				{
					return false;
				}
				for (int i = 0; i < parameters.Length; i++)
				{
					if (!IsParameterTypeMatch(parameters[i].ParameterType, parameterTypes[i]))
					{
						return false;
					}
				}
				return true;
			}).FirstOrDefault();
		}
		return modBehaviour.GetMethod(methodName, bindingAttr);
	}

	private static bool IsParameterTypeMatch(Type parameterType, Type providedType)
	{
		if (parameterType == providedType)
		{
			return true;
		}
		if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == providedType)
		{
			return true;
		}
		if (parameterType.IsAssignableFrom(providedType))
		{
			return true;
		}
		return false;
	}

	private static bool InvokeMethod(string cacheKey, string methodName, object[] parameters, Type delegateType, Type[] paramTypes)
	{
		if (!methodCache.ContainsKey(cacheKey))
		{
			MethodInfo staticPublicMethodInfo = GetStaticPublicMethodInfo(methodName, paramTypes);
			if (staticPublicMethodInfo == null)
			{
				Debug.LogError((object)("找不到此方法" + methodName + ",无法调用"));
				return false;
			}
			methodCache[cacheKey] = Delegate.CreateDelegate(delegateType, staticPublicMethodInfo);
		}
		try
		{
			methodCache[cacheKey].DynamicInvoke(parameters);
			return true;
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("委托调用" + methodName + "失败: " + ex.Message));
			return false;
		}
	}
}
