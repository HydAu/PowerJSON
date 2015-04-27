﻿using System;
using System.Collections.Generic;

namespace fastJSON
{
	delegate object CreateObject ();
	delegate object GenericSetter (object target, object value);
	delegate object GenericGetter (object obj);

	/// <summary>
	/// The cached serialization information used by the reflection engine during serialization and deserialization.
	/// </summary>
	/// <preliminary />
	public sealed class SerializationManager
	{
		static readonly char[] __enumSeperatorCharArray = { ',' };

		readonly SafeDictionary<Type, ReflectionCache> _reflections = new SafeDictionary<Type, ReflectionCache> ();
		readonly IReflectionController _controller;
		internal readonly SafeDictionary<Enum, string> EnumValueCache = new SafeDictionary<Enum, string> ();

		/// <summary>
		/// Returns the <see cref="IReflectionController"/> currently used by the <see cref="SerializationManager"/>. The instance could be casted to concrete type for more functionalities.
		/// </summary>
		public IReflectionController ReflectionController { get { return _controller; } }

		/// <summary>
		/// Gets the singleton instance.
		/// </summary>
		public static readonly SerializationManager Instance = new SerializationManager (new FastJsonReflectionController ());

		/// <summary>
		/// Creates a new instance of <see cref="SerializationManager"/> with a specific <see cref="IReflectionController"/>.
		/// </summary>
		/// <param name="controller">The controller to control object reflections before serialization.</param>
		public SerializationManager (IReflectionController controller) {
			_controller = controller;
		}

		internal ReflectionCache GetDefinition (Type type) {
			ReflectionCache c;
			if (_reflections.TryGetValue (type, out c)) {
				return c;
			}
			return _reflections[type] = new ReflectionCache (type, this);
		}

		/// <summary>
		/// Registers <see cref="ReflectionOverride"/> for the <typeparamref name="T"/> type. If the type is already registered automatically or manually, the <paramref name="overrideInfo"/> will merged into the existing reflected info.
		/// </summary>
		/// <typeparam name="T">The type to be overridden.</typeparam>
		/// <param name="overrideInfo">The override info of the type.</param>
		public void RegisterReflectionOverride<T> (ReflectionOverride overrideInfo) {
			RegisterReflectionOverride (typeof (T), overrideInfo, false);
		}

		/// <summary>
		/// Registers <see cref="ReflectionOverride"/> for the <typeparamref name="T"/> type.
		/// </summary>
		/// <typeparam name="T">The type to be overridden.</typeparam>
		/// <param name="overrideInfo">The override info of the type.</param>
		/// <param name="purgeExisting">If this value is true, the reflection engine will reflect the type again and apply the <paramref name="overrideInfo"/>, otherwise, <paramref name="overrideInfo"/> is merged into the existing reflection cache.</param>
		public void RegisterReflectionOverride<T> (ReflectionOverride overrideInfo, bool purgeExisting) {
			RegisterReflectionOverride (typeof (T), overrideInfo, purgeExisting);
		}

		/// <summary>
		/// Registers <see cref="ReflectionOverride"/> for the specific type.
		/// </summary>
		/// <param name="type">The type to be overridden.</param>
		/// <param name="overrideInfo">The override info of the type.</param>
		/// <param name="purgeExisting">If this value is true, the reflection engine will reflect the type again and apply the <paramref name="overrideInfo"/>, otherwise, <paramref name="overrideInfo"/> is merged into the existing reflection cache.</param>
		public void RegisterReflectionOverride (Type type, ReflectionOverride overrideInfo, bool purgeExisting) {
			var c = purgeExisting ? new ReflectionCache (type, this) : GetDefinition (type);
			if (overrideInfo.OverrideInterceptor) {
				c.Interceptor = overrideInfo.Interceptor;
			}
			MemberOverride mo;
			var s = c.Properties;
			foreach (var g in c.Getters) {
				mo = null;
				foreach (var item in overrideInfo.MemberOverrides) {
					if (item.MemberName == g.MemberName) {
						mo = item;
						break;
					}
				}
				if (mo == null) {
					continue;
				}

				if (mo.Serializable != TriState.Default) {
					g.Serializable = mo.Serializable;
				}

				var ot = mo.OverrideTypedNames;
				if (ot) {
					g.TypedNames = mo.TypedNames;
				}
				var sn = mo.SerializedName;
				if (mo.OverrideSerializedName || mo.OverrideTypedNames) {
					if (sn == g.MemberName) {
						g.SpecificName = g.TypedNames != null && g.TypedNames.Count > 0;
					}
					else {
						g.SpecificName = true;
					}
				}

				if (mo.OverrideConverter) {
					g.Converter = mo.Converter;
				}

				myPropInfo mp = null;
				if (ot) {
					// remove previous polymorphic deserialization info
					var rt = new List<string> ();
					foreach (var item in s) {
						if (item.Value.MemberName == mo.MemberName) {
							if (item.Value.MemberType != g.MemberType) {
								rt.Add (item.Key);
							}
							mp = item.Value;
						}
					}
					if (mp == null) {
						throw new MissingMemberException (g.MemberType.FullName, mo.MemberName);
					}
					foreach (var item in rt) {
						s.Remove (item);
					}
					// add new polymorphic deserialization info
					if (mo.TypedNames.Count > 0) {
						foreach (var item in mo.TypedNames) {
							var t = item.Key;
							if (g.MemberType.IsAssignableFrom (t) == false) {
								throw new InvalidCastException ("The override type (" + t.FullName + ") does not derive from the member type (" + g.MemberType.FullName + ")");
							}
							var n = item.Value;
							var p = new myPropInfo (t, g.MemberName, Reflection.Instance.IsTypeRegistered (t));
							p.Getter = mp.Getter;
							p.Setter = mp.Setter;
							p.CanWrite = mp.CanWrite;
							myPropInfo tp;
							if (s.TryGetValue (n, out tp) && tp.MemberType == g.MemberType) {
								s[n] = p;
							}
							else {
								s.Add (n, p);
							}
						}
					}
				}
				else if (mo.OverrideSerializedName && g.SerializedName != mo.SerializedName) {
					mp = s[g.SerializedName];
					s.Remove (g.SerializedName);
					s.Add (sn, mp);
				}
				foreach (var item in s) {
					mp = item.Value;
					if (mp.MemberName == mo.MemberName) {
						if (mo.OverrideConverter) {
							mp.Converter = mo.Converter;
						}
					}
				}
				if (mo.OverrideSerializedName) {
					g.SerializedName = sn;
				}
			}
			if (purgeExisting) {
				_reflections[type] = c;
			}
		}

		/// <summary>
		/// <para>Assigns an <see cref="IJsonInterceptor"/> to process a specific type.</para>
		/// <para>This is a simplified version of <see cref="RegisterReflectionOverride{T}(ReflectionOverride)"/> method replacing the <see cref="IJsonInterceptor"/> of a type.</para>
		/// </summary>
		/// <typeparam name="T">The type to be processed by the interceptor.</typeparam>
		/// <param name="interceptor">The interceptor to intercept the serialization and deserialization.</param>
		/// <remarks>If the type has already gotten an <see cref="IJsonInterceptor"/>, the new <paramref name="interceptor"/> will replace it. If the new interceptor is null, existing interceptor will be removed from the type.</remarks>
		public void RegisterTypeInterceptor<T> (IJsonInterceptor interceptor) {
			RegisterTypeInterceptor (typeof (T), interceptor);
		}

		/// <summary>
		/// <para>Assigns an <see cref="IJsonInterceptor"/> to process a specific type.</para>
		/// <para>This is a simplified version of <see cref="RegisterReflectionOverride{T}(ReflectionOverride)"/> method replacing the <see cref="IJsonInterceptor"/> of a type.</para>
		/// </summary>
		/// <param name="type">The type to be processed by the interceptor.</param>
		/// <param name="interceptor">The interceptor to intercept the serialization and deserialization.</param>
		/// <remarks>If the type has already gotten an <see cref="IJsonInterceptor"/>, the new <paramref name="interceptor"/> will replace it. If the new interceptor is null, existing interceptor will be removed from the type.</remarks>
		public void RegisterTypeInterceptor (Type type, IJsonInterceptor interceptor) {
			RegisterReflectionOverride (type, new ReflectionOverride () { Interceptor = interceptor }, false);
		}

		/// <summary>
		/// <para>Assigns the serialized name of a field or property.</para>
		/// <para>This is a simplified version of <see cref="RegisterReflectionOverride{T}(ReflectionOverride)"/> method replacing the serialized name of a member.</para>
		/// </summary>
		/// <typeparam name="T">The type containing the member.</typeparam>
		/// <param name="memberName">The name of the field or property.</param>
		/// <param name="serializedName">The serialized name of the member.</param>
		/// <remarks>If <paramref name="serializedName"/> is null or <see cref="String.Empty"/>, the field or property name will be used.</remarks>
		public void RegisterMemberName<T> (string memberName, string serializedName) {
			RegisterMemberName (typeof (T), memberName, serializedName);
		}

		/// <summary>
		/// <para>Assigns the serialized name of a field or property.</para>
		/// <para>This is a simplified version of <see cref="RegisterReflectionOverride{T}(ReflectionOverride)"/> method replacing the serialized name of a member.</para>
		/// </summary>
		/// <param name="type">The type containing the member.</param>
		/// <param name="memberName">The name of the field or property.</param>
		/// <param name="serializedName">The serialized name of the member.</param>
		/// <remarks>If <paramref name="serializedName"/> is null or <see cref="String.Empty"/>, the field or property name will be used.</remarks>
		public void RegisterMemberName (Type type, string memberName, string serializedName) {
			RegisterReflectionOverride (type, new ReflectionOverride () {
				MemberOverrides = { new MemberOverride (memberName, serializedName) }
			}, false);
		}

		/// <summary>
		/// <para>Assigns an <see cref="IJsonConverter"/> to convert the value of the specific member.</para>
		/// <para>This is a simplified version of <see cref="RegisterReflectionOverride{T}(ReflectionOverride)"/> method replacing the <see cref="IJsonConverter"/> of a member.</para>
		/// </summary>
		/// <param name="type">The type containing the member.</param>
		/// <param name="memberName">The member to be assigned.</param>
		/// <param name="converter">The converter to process the member value.</param>
		/// <remarks>If the member has already gotten an <see cref="IJsonConverter"/>, the new <paramref name="converter"/> will replace it. If the new converter is null, existing converter will be removed from the type.</remarks>
		public void RegisterMemberInterceptor (Type type, string memberName, IJsonConverter converter) {
			var c = GetDefinition (type);
			string n = null;
			foreach (var item in c.Getters) {
				if (item.MemberName == memberName) {
					item.Converter = converter;
					n = item.SerializedName;
					break;
				}
			}
			myPropInfo p;
			if (c.Properties.TryGetValue (n, out p)) {
				p.Converter = converter;
			}
		}

		internal string GetEnumName (Enum value) {
			string t;
			if (EnumValueCache.TryGetValue (value, out t)) {
				return t;
			}
			var et = value.GetType ();
			var f = GetDefinition (et);
			if (EnumValueCache.TryGetValue (value, out t)) {
				return t;
			}
			if (f.IsFlaggedEnum) {
				var vs = Enum.GetValues (et);
				var iv = (ulong)Convert.ToInt64 (value);
				var ov = iv;
				if (iv == 0) {
					return "0"; // should not be here
				}
				var sl = new List<string> ();
				var vm = f.EnumNames;
				for (int i = vs.Length - 1; i > 0; i--) {
					var ev = (ulong)Convert.ToInt64 (vs.GetValue (i));
					if (ev == 0) {
						continue;
					}
					if ((iv & ev) == ev) {
						iv -= ev;
						sl.Add (EnumValueCache[(Enum)Enum.ToObject (et, ev)]);
					}
				}
				if (iv != 0) {
					return null;
				}
				sl.Reverse ();
				t = String.Join (",", sl.ToArray ());
				EnumValueCache.Add (value, t);
			}
			return t;
		}

		internal Enum GetEnumValue (Type type, string name) {
			var def = GetDefinition (type);
			Enum e;
			if (def.EnumNames.TryGetValue (name, out e)) {
				return e;
			}
			if (def.IsFlaggedEnum) {
				ulong v = 0;
				var s = name.Split (__enumSeperatorCharArray);
				foreach (var item in s) {
					if (def.EnumNames.TryGetValue (item, out e) == false) {
						throw new KeyNotFoundException ("Key \"" + item + "\" not found for type " + type.FullName);
					}
					v |= Convert.ToUInt64 (e);
				}
				return (Enum)Enum.ToObject (type, v);
			}
			throw new KeyNotFoundException ("Key \"" + name + "\" not found for type " + type.FullName);
		}
	}

	/// <summary>
	/// Contains reflection overriding information, used in type reflection phase before serialization or deserialization.
	/// </summary>
	/// <preliminary />
	public class ReflectionOverride
	{
		internal bool OverrideInterceptor;
		IJsonInterceptor _Interceptor;
		/// <summary>
		/// Gets or sets the <see cref="IJsonInterceptor"/> for the member.
		/// </summary>
		public IJsonInterceptor Interceptor {
			get { return _Interceptor; }
			set {
				_Interceptor = value; OverrideInterceptor = true;
			}
		}

		List<MemberOverride> _MemberOverrides;
		/// <summary>
		/// Specifies the override for members.
		/// </summary>
		public List<MemberOverride> MemberOverrides {
			get {
				if (_MemberOverrides == null) {
					_MemberOverrides = new List<MemberOverride> ();
				}
				return _MemberOverrides;
			}
			set { _MemberOverrides = value; }
		}
	}

	/// <summary>
	/// Contains reflection override settings for a member.
	/// </summary>
	/// <preliminary />
	public class MemberOverride
	{
		/// <summary>
		/// Gets the name of the overridden member.
		/// </summary>
		public string MemberName { get; private set; }

		internal bool OverrideSerializedName;
		string _SerializedName;
		/// <summary>
		/// Gets or sets the serialized name for the member.
		/// </summary>
		public string SerializedName {
			get { return _SerializedName; }
			set { _SerializedName = value; OverrideSerializedName = true; }
		}

		internal bool OverrideConverter;
		IJsonConverter _Converter;
		/// <summary>
		/// Gets or sets the <see cref="IJsonConverter"/> for the member.
		/// </summary>
		/// <remarks>If the member has a converter before the override, and the value of this converter is null, existing converter will be removed after the override.</remarks>
		public IJsonConverter Converter {
			get { return _Converter; }
			set { _Converter = value; OverrideConverter = true; }
		}

		internal bool OverrideTypedNames {
			get { return _TypedNames != null && _TypedNames.Count > 0; }
		}
		Dictionary<Type, string> _TypedNames;
		/// <summary>
		/// Gets or sets the polymorphic serialization for the member. The item key is the type and the item value is the serialized name corrsponding to the type. The type should derive from the type of the member.
		/// </summary>
		public Dictionary<Type, string> TypedNames {
			get {
				if (_TypedNames == null) {
					_TypedNames = new Dictionary<Type, string> ();
				}
				return _TypedNames;
			}
			set {
				_TypedNames = value;
			}
		}

		/// <summary>
		/// Denotes whether the member is always serialized (<see cref="TriState.True"/>), never serialized (<see cref="TriState.False"/>) or compliant to the existing behavior (<see cref="TriState.Default"/>).
		/// </summary>
		public TriState Serializable { get; set; }

		/// <summary>
		/// Creates an instance of <see cref="MemberOverride"/>. The override info can be set via the properties.
		/// </summary>
		/// <param name="memberName">The name of the member.</param>
		/// <remarks>The member name is case sensitive during serialization, and case-insensitive during deserialization.</remarks>
		public MemberOverride (string memberName) {
			MemberName = memberName;
		}
		/// <summary>
		/// Creates an instance of <see cref="MemberOverride"/>, setting the <see cref="Serializable"/> property. The other override info can be set via the properties.
		/// </summary>
		/// <param name="memberName">The name of the member.</param>
		/// <param name="serializable">How the member is serialized.</param>
		/// <remarks>The member name is case sensitive during serialization, and case-insensitive during deserialization.</remarks>
		public MemberOverride (string memberName, TriState serializable) : this(memberName) {
			Serializable = serializable;
		}
		/// <summary>
		/// Creates an instance of <see cref="MemberOverride"/>, setting the <see cref="Converter"/> property. The other override info can be set via the properties.
		/// </summary>
		/// <param name="memberName">The name of the member.</param>
		/// <param name="converter">The converter.</param>
		/// <remarks>
		/// <para>The member name is case sensitive during serialization, and case-insensitive during deserialization.</para>
		/// </remarks>
		public MemberOverride (string memberName, IJsonConverter converter) : this (memberName) {
			Converter = converter;
		}
		/// <summary>
		/// Creates an instance of <see cref="MemberOverride"/>, setting the <see cref="SerializedName"/> property. The other override info can be set via the properties.
		/// </summary>
		/// <param name="memberName">The name of the member.</param>
		/// <param name="serializedName">The serialized name of the member.</param>
		/// <remarks>
		/// <para>The member name is case sensitive during serialization, and case-insensitive during deserialization.</para>
		/// </remarks>
		public MemberOverride (string memberName, string serializedName) : this (memberName) {
			SerializedName = serializedName;
		}
	}
}